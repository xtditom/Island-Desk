# Application icon

`IslandDesk.png` is the original 1254 × 1254 transparent artwork. `IslandDesk.ico` is its Windows application icon, with 16, 24, 32, 48, 64, 128 and 256 pixel images. Each size preserves the full square canvas, aspect ratio and alpha transparency, using Lanczos downsampling.

The actual application project is `IslandDesk.csproj`. Its `ApplicationIcon` and WPF `Resource` entries embed the ICO in Debug, Release and published builds. Settings, applicable window/taskbar icons, and the notification-area icon use that shared resource. The island's vector pet is independent.

No loose icon file or image conversion dependency is needed at runtime. To regenerate after replacing the source PNG, run this from the application project folder with Python and Pillow installed:

```powershell
python -c "from PIL import Image; im = Image.open('Assets/IslandDesk.png').convert('RGBA'); im.save('Assets/IslandDesk.ico', format='ICO', sizes=[(s,s) for s in (16,24,32,48,64,128,256)])"
```

Rebuild after replacing the ICO. The existing conditional project entries retain the generated tray icon and default window icon if the asset is intentionally absent. Windows may cache Explorer/taskbar icons for an existing executable path; a stale cached icon does not indicate that the build omitted the new resource.
