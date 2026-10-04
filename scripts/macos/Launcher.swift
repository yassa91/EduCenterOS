import AppKit

final class Launcher: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private var window: NSWindow!
    private let status = NSTextField(wrappingLabelWithString: "جاري تجهيز التشغيل…")
    private let detail = NSTextField(wrappingLabelWithString: "سيتم فتح Swagger عندما يصبح التطبيق جاهزًا.")
    private let progress = NSProgressIndicator()
    private let openButton = NSButton(title: "فتح Swagger", target: nil, action: nil)
    private let stopButton = NSButton(title: "إلغاء التشغيل", target: nil, action: nil)
    private var process: Process?
    private var outputBuffer = ""
    private var swaggerURL: URL?
    private var ready = false
    private var failed = false
    private var closing = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        let menu = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "إغلاق EduCenterOS", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        menu.addItem(appItem)
        NSApp.mainMenu = menu

        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 280),
                          styleMask: [.titled, .closable, .miniaturizable], backing: .buffered, defer: false)
        window.title = "EduCenterOS"
        window.delegate = self
        window.isReleasedWhenClosed = false
        status.font = .systemFont(ofSize: 19, weight: .semibold)
        status.alignment = .right
        detail.textColor = .secondaryLabelColor
        detail.alignment = .right
        progress.style = .bar
        progress.isIndeterminate = true
        progress.startAnimation(nil)
        openButton.target = self
        openButton.action = #selector(openSwagger)
        openButton.isEnabled = false
        stopButton.target = self
        stopButton.action = #selector(stopOrRetry)
        openButton.bezelStyle = .rounded
        stopButton.bezelStyle = .rounded
        let buttons = NSStackView(views: [openButton, stopButton])
        buttons.orientation = .horizontal
        buttons.spacing = 12
        let stack = NSStackView(views: [status, detail, progress, buttons])
        stack.orientation = .vertical
        stack.alignment = .trailing
        stack.spacing = 18
        stack.translatesAutoresizingMaskIntoConstraints = false
        window.contentView!.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor, constant: 28),
            stack.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor, constant: -28),
            stack.topAnchor.constraint(equalTo: window.contentView!.topAnchor, constant: 28),
            status.widthAnchor.constraint(equalTo: stack.widthAnchor),
            detail.widthAnchor.constraint(equalTo: stack.widthAnchor),
            progress.widthAnchor.constraint(equalTo: stack.widthAnchor)
        ])
        window.center()
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        start()
    }

    private func start() {
        guard let root = Bundle.main.object(forInfoDictionaryKey: "EduCenterOSProjectRoot") as? String else { return }
        ready = false
        failed = false
        outputBuffer = ""
        swaggerURL = nil
        openButton.isEnabled = false
        stopButton.title = "إلغاء التشغيل"
        stopButton.isEnabled = true
        status.stringValue = "جاري تجهيز التشغيل…"
        detail.stringValue = "سيتم فتح Swagger عندما يصبح التطبيق جاهزًا."
        progress.isHidden = false
        progress.startAnimation(nil)
        let child = Process()
        child.executableURL = URL(fileURLWithPath: "/usr/bin/env")
        child.arguments = ["python3", root + "/scripts/launch_macos.py"]
        child.currentDirectoryURL = URL(fileURLWithPath: root)
        var environment = ProcessInfo.processInfo.environment
        let home = NSHomeDirectory()
        let tools = [home + "/.pyenv/shims", home + "/.pyenv/bin", "/opt/homebrew/bin", "/usr/local/share/dotnet", "/usr/local/bin", "/Library/Frameworks/Python.framework/Versions/Current/bin", "/usr/bin", "/bin", "/usr/sbin", "/sbin"]
        environment["PATH"] = (tools + [environment["PATH"] ?? ""]).joined(separator: ":")
        child.environment = environment
        let pipe = Pipe()
        child.standardOutput = pipe
        child.standardError = FileHandle.nullDevice
        child.standardInput = FileHandle.nullDevice
        pipe.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let bytes = handle.availableData
            if bytes.isEmpty { handle.readabilityHandler = nil; return }
            guard let text = String(data: bytes, encoding: .utf8) else { return }
            DispatchQueue.main.async { self?.receive(text) }
        }
        child.terminationHandler = { [weak self] finished in
            DispatchQueue.main.async {
                guard let self = self else { return }
                self.process = nil
                self.progress.stopAnimation(nil)
                self.progress.isHidden = true
                if self.closing { NSApp.reply(toApplicationShouldTerminate: true); return }
                if !self.ready && !self.failed && finished.terminationStatus != 0 {
                    self.status.stringValue = "تعذر بدء التشغيل"
                    self.detail.stringValue = "تأكد من وجود Python وإعداد الجهاز حسب README."
                    self.failed = true
                }
                self.stopButton.isEnabled = true
                self.stopButton.title = self.ready ? "إغلاق النافذة" : "إعادة التشغيل"
            }
        }
        do {
            try child.run()
            process = child
        } catch {
            status.stringValue = "تعذر بدء التشغيل"
            detail.stringValue = "تأكد من وجود Python وإعداد الجهاز حسب README."
            progress.stopAnimation(nil)
            progress.isHidden = true
            stopButton.title = "إعادة التشغيل"
        }
    }

    private func receive(_ text: String) {
        outputBuffer += text
        while let end = outputBuffer.firstIndex(of: "\n") {
            let line = String(outputBuffer[..<end])
            outputBuffer.removeSubrange(...end)
            guard let data = line.data(using: .utf8),
                  let event = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let kind = event["kind"] as? String,
                  let message = event["message"] as? String else { continue }
            status.stringValue = message
            if kind == "ready", let address = event["url"] as? String, let url = URL(string: address) {
                ready = true
                swaggerURL = url
                openButton.isEnabled = true
                progress.stopAnimation(nil)
                progress.isHidden = true
                if event["owned"] as? Bool == true {
                    stopButton.title = "إيقاف التطبيق"
                    detail.stringValue = "زر الإيقاف أو إغلاق النافذة يوقف الـ API. قاعدة البيانات تظل متاحة."
                } else {
                    stopButton.title = "إغلاق النافذة"
                    detail.stringValue = "النسخة الحالية ستظل تعمل عند إغلاق هذه النافذة. لم يتم إعادة بنائها."
                }
                NSWorkspace.shared.open(url)
            } else if kind == "error" || kind == "stopped" {
                failed = kind == "error"
                ready = false
                openButton.isEnabled = false
                detail.stringValue = kind == "error" ? "أصلح السبب المذكور، ثم اضغط إعادة التشغيل." : "يمكنك تشغيل التطبيق مرة أخرى من نفس النافذة."
                progress.stopAnimation(nil)
                progress.isHidden = true
            }
        }
    }

    @objc private func openSwagger() {
        if let url = swaggerURL { NSWorkspace.shared.open(url) }
    }

    @objc private func stopOrRetry() {
        if let child = process, child.isRunning {
            stopButton.isEnabled = false
            status.stringValue = "جاري الإيقاف…"
            child.interrupt()
        } else if ready {
            NSApp.terminate(nil)
        } else {
            start()
        }
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        NSApp.terminate(nil)
        return false
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard let child = process, child.isRunning else { return .terminateNow }
        closing = true
        child.interrupt()
        return .terminateLater
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        window.makeKeyAndOrderFront(nil)
        return true
    }
}

let app = NSApplication.shared
let launcher = Launcher()
app.setActivationPolicy(.regular)
app.delegate = launcher
app.run()
