namespace FolderMenuX;

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;

public static class ConfigReader
{
    public static string GetRootPath()
    {
        string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string configPath = Path.Combine(exeDirectory, "config.ini");

        if (!File.Exists(configPath))
        {
            // Use system-safe user directory
            string defaultUserPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            try
            {
                File.WriteAllLines(configPath, new[]
                {
                    "[Settings]",
                    $"RootPath={defaultUserPath}"
                });

                MessageBox.Show($"config.ini was missing and has been created with default path:\n{defaultUserPath}",
                    "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create config.ini:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return null!;
            }
        }


        foreach (var line in File.ReadAllLines(configPath))
        {
            if (line.StartsWith("RootPath=", StringComparison.OrdinalIgnoreCase))
            {
                var path = line.Substring("RootPath=".Length).Trim();
                if (!string.IsNullOrWhiteSpace(path))
                    return path;
            }
        }

        MessageBox.Show("RootPath not found in config.ini.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Exit();
        return null!;
    }
}
public static class IconHelper
{
    private const int SHGFI_ICON = 0x100;
    private const int SHGFI_SMALLICON = 0x1;
    private const int FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, int dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, int uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public static Icon GetSmallIcon(string filePath)
    {
        SHFILEINFO shfi;
        IntPtr result = SHGetFileInfo(
            filePath,
            FILE_ATTRIBUTE_NORMAL,
            out shfi,
            (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
            SHGFI_ICON | SHGFI_SMALLICON
        );

        if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
        {
            // Clone the icon to safely keep a managed copy
            Icon clonedIcon = (Icon)Icon.FromHandle(shfi.hIcon).Clone();
            DestroyIcon(shfi.hIcon); // Now it's safe to destroy the original handle
            return clonedIcon;
        }

        return null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

static class Program
{
    /// <summary>
    /// FolderMenuX — Bring Back the Windows 10 Taskbar Folder Menu (for Windows 11)
    /// </summary>

    // Your root folder path
    private static readonly string RootPath = ConfigReader.GetRootPath();

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var menu = new ContextMenuStrip();
        menu.ShowImageMargin = true;
        menu.RenderMode = ToolStripRenderMode.System;
		
        menu.Closed += (s, e) =>
        {
            // Check if the menu closed because the user clicked somewhere else
            // or for any other reason besides a menu item being clicked.
            if (e.CloseReason != ToolStripDropDownCloseReason.ItemClicked)
            {
                Application.Exit();
            }
        };
        
        menu.KeyPress += (s, e) =>
        {
            // Close the application if the user presses Escape
            if (e.KeyChar == (char)Keys.Escape)
            {
                Application.Exit();
            }
        };

        if (Directory.Exists(RootPath))
        {
            PopulateTopLevelFolders(RootPath, menu.Items);
        }
        else
        {
            menu.Items.Add("Error: Root folder not found");
        }

        Timer timer = new Timer { Interval = 100 };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            menu.Show(Cursor.Position);
        };
        timer.Start();

        Application.Run();
    }

    private static void CloseAppWithDelay()
    {
        // A timer to ensure the application exits after a short delay,
        // giving the new process (file, folder, exe) time to start.
        Timer exitTimer = new Timer { Interval = 100 };
        exitTimer.Tick += (s, e) =>
        {
            exitTimer.Stop();
            Application.Exit();
        };
        exitTimer.Start();
    }

    private static void PopulateTopLevelFolders(string rootPath, ToolStripItemCollection items)
    {
        foreach (var dir in Directory.GetDirectories(rootPath))
        {
            var folderItem = CreateFolderMenuItem(dir);
            items.Add(folderItem);
        }

        foreach (var file in Directory.GetFiles(rootPath))
        {
            var fileItem = CreateFileMenuItem(file);
            items.Add(fileItem);
        }
    }

    private static ToolStripMenuItem CreateFolderMenuItem(string folderPath)
    {
        var dirName = Path.GetFileName(folderPath);
        var folderItem = new ToolStripMenuItem(dirName) { Tag = folderPath };


        // Open folder on click
        folderItem.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true });
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open folder:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            CloseAppWithDelay();
        };

        // Add a dummy item to indicate it's expandable
        folderItem.DropDownItems.Add("Loading...");

        folderItem.DropDownOpening += (s, e) =>
        {
            var item = s as ToolStripMenuItem;
            if (item == null || item.DropDownItems.Count != 1 || item.DropDownItems[0].Text != "Loading...")
                return;

            item.DropDownItems.Clear(); // Remove dummy
            try
            {
                // Subfolders
                foreach (var subDir in Directory.GetDirectories(folderPath))
                {
                    var subFolderItem = CreateFolderMenuItem(subDir);
                    item.DropDownItems.Add(subFolderItem);
                }

                // Files
                foreach (var file in Directory.GetFiles(folderPath))
                {
                    var fileItem = CreateFileMenuItem(file);
                    item.DropDownItems.Add(fileItem);
                }
            }
            catch (Exception ex)
            {
                item.DropDownItems.Add($"Error: {ex.Message}");
            }
        };

        return folderItem;
    }

    private static ToolStripMenuItem CreateFileMenuItem(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var fileItem = new ToolStripMenuItem(fileName) { Tag = filePath };

        try
        {
            // Get the system icon for the file
            //Icon fileIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            // Convert the icon to a Bitmap and assign it to the menu item's Image property
            //fileItem.Image = fileIcon.ToBitmap();
            Icon fileIcon = IconHelper.GetSmallIcon(filePath);
            if (fileIcon != null)
            {
                fileItem.Image = fileIcon.ToBitmap();
            }
        }
        catch (Exception)
        {
            // If getting the icon fails for some reason, just skip it.
        }

        fileItem.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open file:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            CloseAppWithDelay();
        };
        return fileItem;
    }
}