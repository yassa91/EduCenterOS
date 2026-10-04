import contextlib
import importlib.util
import io
import json
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("macos_launcher", ROOT / "scripts/launch_macos.py")
launcher = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(launcher)


class LauncherTests(unittest.TestCase):
    def setUp(self):
        launcher.STOP.clear()
        self.trust = patch.object(launcher, "trusted_https_opener", return_value=None)
        self.trust.start()
        self.addCleanup(self.trust.stop)

    @contextlib.contextmanager
    def server(self, educenteros):
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                if self.path in ("/health/live", "/health/ready"):
                    payload = b"Healthy"
                else:
                    payload = json.dumps({"info": {"title": "EduCenterOS API" if educenteros else "Other API"},
                                          "paths": {"/api/v1/accounts": {}}}).encode()
                self.send_response(200)
                self.end_headers()
                self.wfile.write(payload)

            def log_message(self, *_):
                pass

        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            yield server.server_port
        finally:
            server.shutdown()
            server.server_close()
            thread.join()

    def test_existing_api_is_reused_without_running_bootstrap_commands(self):
        with self.server(True) as port, patch.object(launcher, "read_port", return_value=port), \
                patch.object(launcher, "secure_api_ready", return_value=True), \
                patch.object(launcher, "run_step") as step, contextlib.redirect_stdout(io.StringIO()) as output:
            launcher.launch()
            event = json.loads(output.getvalue())
            self.assertEqual("ready", event["kind"])
            self.assertFalse(event["owned"])
            step.assert_not_called()

    def test_http_only_instance_is_not_reused_as_secure_ready(self):
        with self.server(True) as port, patch.object(launcher, "read_port", return_value=port), \
                patch.object(launcher, "secure_api_ready", return_value=False), patch.object(launcher, "run_step") as step:
            with self.assertRaises(RuntimeError): launcher.launch()
            self.assertTrue(launcher.occupied(port))
            step.assert_not_called()

    def test_foreign_application_is_left_running(self):
        with self.server(False) as port, patch.object(launcher, "read_port", return_value=port), \
                patch.object(launcher, "run_step") as step:
            with self.assertRaises(RuntimeError):
                launcher.launch()
            self.assertTrue(launcher.occupied(port))
            step.assert_not_called()

    def test_failed_command_does_not_disclose_subprocess_output(self):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            with self.assertRaisesRegex(RuntimeError, "safe failure"):
                launcher.run_step([sys.executable, "-c", "print('private-marker'); raise SystemExit(1)"],
                                  "status", "safe failure", timeout=5)
            self.assertNotIn("private-marker", output.getvalue())

    def test_stop_terminates_owned_child_and_leaves_unrelated_process_alive(self):
        with tempfile.TemporaryFile() as output:
            owned = launcher.start_process([sys.executable, "-c", "import time; time.sleep(60)"], output)
            unrelated = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"],
                                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            try:
                time.sleep(0.15)
                launcher.stop_owned_process(owned)
                self.assertIsNotNone(owned.poll())
                self.assertIsNone(unrelated.poll())
            finally:
                if owned.poll() is None:
                    owned.kill()
                owned.wait()
                unrelated.terminate()
                unrelated.wait()

    def test_cancel_interrupts_command_and_waits_for_its_cleanup(self):
        timer = threading.Timer(0.2, launcher.STOP.set)
        timer.start()
        try:
            with contextlib.redirect_stdout(io.StringIO()), self.assertRaises(InterruptedError):
                launcher.run_step([sys.executable, "-c", "import time; time.sleep(60)"], "status", "failed", timeout=5)
        finally:
            timer.cancel()
            timer.join()


if __name__ == "__main__":
    unittest.main()
