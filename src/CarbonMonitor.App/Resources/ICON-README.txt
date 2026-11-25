Carbon Monitor Application Icon
================================

Place carbon-monitor.ico here. The icon should be a multi-resolution .ico file
containing the following sizes:

  - 16x16   (small icon, taskbar, system tray)
  - 32x32   (standard icon, Alt+Tab)
  - 48x48   (large icon, Explorer details)
  - 256x256 (extra-large icon, Explorer tiles, high-DPI)

The icon is referenced by:
  - The application project (CarbonMonitor.App.csproj) as the executable icon
  - The Inno Setup installer script (installer/CarbonMonitor.iss) as the setup icon
  - The system tray notification icon at runtime

For development, the app will use a default system icon if this file is missing.
You can generate an icon using any icon editor or online tool, for example:
  - GIMP (https://www.gimp.org/) — export as .ico with multiple layers
  - RealWorld Icon Editor (http://www.rw-designer.com/)
  - IcoFX (https://icofx.ro/)
  - Online: https://convertico.com/ or https://www.icoconverter.com/

Recommended design guidelines:
  - Use a simple, recognizable shape that works at all sizes
  - Ensure the 16x16 version is still readable (avoid fine detail)
  - Use a transparent background for a modern look
  - Consider the system tray context: the icon should be distinct at 16x16
