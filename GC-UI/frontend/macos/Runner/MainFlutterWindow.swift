import Cocoa
import FlutterMacOS

class MainFlutterWindow: NSWindow {
  override func awakeFromNib() {
    let flutterViewController = FlutterViewController()
    let windowFrame = self.frame
    self.contentViewController = flutterViewController
    self.setFrame(windowFrame, display: true)

    // 去掉系统标题栏（应用名 + 红绿灯按钮），改由 Flutter 自绘：
    //   fullSizeContentView 让内容铺满整窗，titlebarAppearsTransparent + titleVisibility
    //   隐藏标题栏文字与背景，三个系统按钮一并隐藏。
    //   注意：不要动 styleMask 里的 .resizable，否则窗口无法缩放。
    self.titleVisibility = .hidden
    self.titlebarAppearsTransparent = true
    self.styleMask.insert(.fullSizeContentView)
    self.standardWindowButton(.closeButton)?.isHidden = true
    self.standardWindowButton(.miniaturizeButton)?.isHidden = true
    self.standardWindowButton(.zoomButton)?.isHidden = true
    self.isMovableByWindowBackground = true

    // 自绘按钮 -> 原生窗口操作
    let channel = FlutterMethodChannel(
      name: "gc/window",
      binaryMessenger: flutterViewController.engine.binaryMessenger
    )
    channel.setMethodCallHandler { [weak self] call, result in
      guard let self = self else {
        result(FlutterMethodNotImplemented)
        return
      }
      switch call.method {
      case "minimize":
        self.miniaturize(nil)
        result(nil)
      case "toggleMaximize":
        // NSWindow.zoom(_:) 自带切换语义：放大状态下再调即还原
        self.zoom(nil)
        result(nil)
      case "close":
        self.close()
        result(nil)
      case "isMaximized":
        result(self.isZoomed)
      case "beginDrag":
        // 拖拽整窗：用系统拖拽，比在 Dart 里逐帧挪窗口平滑
        if let event = NSApp.currentEvent {
          self.performDrag(with: event)
        }
        result(nil)
      default:
        result(FlutterMethodNotImplemented)
      }
    }

    RegisterGeneratedPlugins(registry: flutterViewController)

    super.awakeFromNib()
  }
}
