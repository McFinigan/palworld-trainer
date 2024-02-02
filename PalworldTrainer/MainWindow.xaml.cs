using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static System.BitConverter;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PalworldTrainer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const string PROC_NAME = "Palworld-Win64-Shipping";

        // Offsets of pointers to traverse to get ammo address
        const Int32 AMMO_STATIC_OFFSET = 0x0894DEC0;
        const Int32 DEFAULT_AMMO_VALUE = 500;
        Int32 ammoValue = DEFAULT_AMMO_VALUE;

        const Int32 WEAPON_DUR_STATIC_OFFSET = 0x08357700;
        const float DEFAULT_WEAPON_DUR_VALUE = 1000;
        float weaponDurValue = DEFAULT_WEAPON_DUR_VALUE;

        const int PROCESS_ALL_ACCESS = 0x1F0FFF;

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(int hProcess,
            long lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(int hProcess,
            long lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesWritten);

        CancellationToken token { get; set; }
        CancellationTokenSource? source { get; set; } = null;

        enum LOGLEVEL
        {
            INFO,
            WARN,
            ERROR,
            SUCCESS
        }
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(ammoTxt.Text, out ammoValue))
            {
                LogMessage($"Ammo value \"{ammoTxt.Text}\" is invalid, set to {DEFAULT_AMMO_VALUE}", LOGLEVEL.ERROR);
                ammoValue = DEFAULT_AMMO_VALUE;
                ammoTxt.Text = DEFAULT_AMMO_VALUE.ToString();
            }

            if (!float.TryParse(weaponDurTxt.Text, out weaponDurValue))
            {
                LogMessage($"Weapon durability value \"{weaponDurTxt.Text}\" is invalid, set to {DEFAULT_WEAPON_DUR_VALUE}", LOGLEVEL.ERROR);
                weaponDurValue = DEFAULT_WEAPON_DUR_VALUE;
                weaponDurTxt.Text = DEFAULT_WEAPON_DUR_VALUE.ToString();
            }

            if (source == null)
            {
                source = new CancellationTokenSource();
                token = source.Token;
                startStopBtn.Content = "Stop";

                await Task.Run(async () =>
                {
                    try
                    {
                        Process[] procList = Process.GetProcessesByName(PROC_NAME);

                        while (!token.IsCancellationRequested && !procList.Any())
                        {
                            Dispatcher.Invoke(() =>
                            {
                                LogMessage($"{PROC_NAME}.exe is not running", LOGLEVEL.ERROR);
                            });

                            await Task.Delay(1000, token);

                            procList = Process.GetProcessesByName(PROC_NAME);
                        }

                        token.ThrowIfCancellationRequested();

                        var proc = procList[0];
                        IntPtr procHandle = OpenProcess(PROCESS_ALL_ACCESS, false, proc.Id);

                        Dispatcher.Invoke(() =>
                        {
                            LogMessage($"Found {PROC_NAME}.exe -> {procHandle}", LOGLEVEL.SUCCESS);
                        });

                        while (!token.IsCancellationRequested)
                        {
                            if (proc.HasExited || proc.MainModule == null)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    LogMessage("PalWorld process has closed", LOGLEVEL.ERROR);
                                });
                                
                                throw new TaskCanceledException();
                            }

                            var currentAmmo = GetAmmoValue(proc.MainModule.BaseAddress, procHandle);

                            if (currentAmmo != ammoValue)
                            {
                                SetAmmoValue(proc.MainModule.BaseAddress, procHandle);

                                Dispatcher.Invoke(() =>
                                {
                                    LogMessage($"Set ammo to {ammoValue}", LOGLEVEL.SUCCESS);
                                });
                            }

                            var currentWeaponDur = GetWeaponDurValue(proc.MainModule.BaseAddress, procHandle);

                            if (currentWeaponDur != weaponDurValue)
                            {
                                SetWeaponDurValue(proc.MainModule.BaseAddress, procHandle);

                                Dispatcher.Invoke(() =>
                                {
                                    LogMessage($"Set weapon durability to {weaponDurValue}", LOGLEVEL.SUCCESS);
                                });
                            }

                            await Task.Delay(200, token);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        CancelExecution();
                        return;
                    }
                }, token);
            }
            else
            {
                CancelExecution();
            }

            startStopBtn.Content = "Start";
        }

        private async void CancelExecution()
        {
            if (source != null && !source.IsCancellationRequested)
            {
                await source.CancelAsync();
                source.Dispose();
                source = null;
            }
        }

        private void LogMessage(string msg, LOGLEVEL level = LOGLEVEL.INFO)
        {
            SolidColorBrush color = level switch
            {
                LOGLEVEL.INFO => Brushes.LightSkyBlue,
                LOGLEVEL.WARN => Brushes.Goldenrod,
                LOGLEVEL.ERROR => Brushes.Crimson,
                LOGLEVEL.SUCCESS => Brushes.LimeGreen,
                _ => Brushes.Purple,
            };

            Run run = new($"[{level}] {DateTime.Now} {msg}\r\n")
            {
                Foreground = color
            };

            logText.Inlines.Add(run);
            scroller.ScrollToEnd();
        }

        public static Int64 GetAmmoAddr(nint baseAddr, IntPtr procHandle)
        {
            int bytesRead = 0;
            byte[] buffer = new byte[8];

            nint addr = baseAddr + AMMO_STATIC_OFFSET;

            ReadProcessMemory((int)procHandle, addr, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x0, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x20, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x210, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x120, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x470, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x4A0, buffer, buffer.Length, ref bytesRead);

            return ToInt64(buffer) + 0x7C;
        }

        public Int32 GetAmmoValue(nint baseAddr, IntPtr procHandle)
        {
            byte[] buffer = new byte[4];
            int bytesRead = 0;

            ReadProcessMemory((int)procHandle, GetAmmoAddr(baseAddr, procHandle), buffer, buffer.Length, ref bytesRead);

            return ToInt32(buffer);
        }

        public void SetAmmoValue(nint baseAddr, IntPtr procHandle)
        {
            byte[] buffer = GetBytes(ammoValue);
            int bytesWritten = 0;

            WriteProcessMemory((int)procHandle, GetAmmoAddr(baseAddr, procHandle), buffer, buffer.Length, ref bytesWritten);
        }

        public static Int64 GetWeaponDurAddr(nint baseAddr, IntPtr procHandle)
        {
            int bytesRead = 0;
            byte[] buffer = new byte[8];

            nint addr = baseAddr + WEAPON_DUR_STATIC_OFFSET;

            ReadProcessMemory((int)procHandle, addr, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x170, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x20, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x20, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x690, buffer, buffer.Length, ref bytesRead);
            ReadProcessMemory((int)procHandle, ToInt64(buffer) + 0x140, buffer, buffer.Length, ref bytesRead);

            return ToInt64(buffer) + 0x70;
        }

        public float GetWeaponDurValue(nint baseAddr, IntPtr procHandle)
        {
            byte[] buffer = new byte[4];
            int bytesRead = 0;

            ReadProcessMemory((int)procHandle, GetWeaponDurAddr(baseAddr, procHandle), buffer, buffer.Length, ref bytesRead);

            return ToSingle(buffer);
        }

        public void SetWeaponDurValue(nint baseAddr, IntPtr procHandle)
        {
            byte[] buffer = GetBytes(weaponDurValue);
            int bytesWritten = 0;

            WriteProcessMemory((int)procHandle, GetWeaponDurAddr(baseAddr, procHandle), buffer, buffer.Length, ref bytesWritten);
        }
    }
}