using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;

namespace RustServerManager
{
    public partial class Form1 : Form
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessInformation(IntPtr hProcess, int ProcessInformationClass, ref PROCESS_POWER_THROTTLING_STATE ProcessInformation, int ProcessInformationSize);

        const uint IDLE_PRIORITY_CLASS = 0x00000040;
        const int ProcessPowerThrottling = 19;
        const int PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        const int PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x00000001;

        struct PROCESS_POWER_THROTTLING_STATE
        {
            public int Version;
            public int ControlMask;
            public int StateMask;
        }

        static void SetProcessPriority()
        {
            SetPriorityClass(GetCurrentProcess(), IDLE_PRIORITY_CLASS);
        }

        static void EnableEcoQos()
        {
            PROCESS_POWER_THROTTLING_STATE PowerThrottling = new PROCESS_POWER_THROTTLING_STATE();
            PowerThrottling.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
            PowerThrottling.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
            PowerThrottling.StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref PowerThrottling, Marshal.SizeOf(PowerThrottling));
        }

        Random rnd = new Random();
        Process steamcmd_process = new Process();
        Process rust_dedicated_process = new Process();

        ClientWebSocket ws;

        List<WipeSchedule> wipe_schedule_list;
        List<MessageSchedule> message_schedule_list;
        string[] wipe_type_names = new string[]{
            "Map",
            "Blueprint",
            "Full"
        };

        List<Plugin> plugin_list = new List<Plugin>();

        public enum InternalStatus
        {
            Stopped = 0,
            Starting = 1,
            Running = 2,
            Stopping = 3,
        }

        InternalStatus server_status_internal = InternalStatus.Stopped;

        System.Timers.Timer timer1 = new System.Timers.Timer();
        System.Timers.Timer hide_timer = new System.Timers.Timer();
        System.Timers.Timer hide_stop_timer = null;

        System.Timers.Timer wipe_timer = new System.Timers.Timer();
        System.Timers.Timer wipe_failed_timer = new System.Timers.Timer();
        System.Timers.Timer auto_wipe_timer = new System.Timers.Timer();
        System.Timers.Timer status_timer = new System.Timers.Timer();
        System.Timers.Timer restart_timer = new System.Timers.Timer();
        int wipe_failed_type = 0;
        int wipe_timed_type = 0;
        bool wipe_timed_after_quit = false;
        bool wipe_failed_twice = false;

        int last_log_line = 0;
        bool started = false;

        bool expect_status_packet = false;
        bool expect_perms_packet = false;

        bool use_oxide = false;
        bool must_update_oxide = false;

        bool should_restart = false;
        bool should_restart_full = false;

        bool wipe_map = false;
        bool wipe_player = false;
        bool wipe_full = false;

        private const int SW_HIDE = 0;
        [DllImport("User32")]
        private static extern int ShowWindow(int hwnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        static extern UInt32 GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);
        private const int WM_VSCROLL = 277;
        private const int SB_PAGEBOTTOM = 7;

        internal static void ScrollToBottom(RichTextBox richTextBox)
        {
            SendMessage(richTextBox.Handle, WM_VSCROLL, (IntPtr)SB_PAGEBOTTOM, IntPtr.Zero);
            richTextBox.SelectionStart = richTextBox.Text.Length;
        }

        public Form1()
        {
            try
            {
                //Enable efficiency mode
                SetProcessPriority();
                EnableEcoQos();
            }
            catch (Exception e) { }

            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            //Empty settings, create before initializing form.
            if (GetSetting("steamcmd_path") == "")
            {
                SetSetting("steamcmd_path", "C:\\steamcmd\\steamcmd.exe");
                SetSetting("assembly_last_hash", "12345");
                SetSetting("server_level", "Procedural Map");
                SetSetting("server_seed", "" + rnd.Next(1, 1000000000));
                SetSetting("server_seed_url", "");
                SetSetting("server_worldsize", "3000");
                SetSetting("server_maxplayers", "75");
                SetSetting("server_hostname", "[REGION] Variables {last_wipe_dm} or {last_wipe_md} will show the last wipe date (d/M and M/d formats).");
                SetSetting("server_description", "Your server description, good for rules.");
                SetSetting("server_url", "https://yourwebsite.com");
                SetSetting("server_headerimage", "https://s3-assets.nodecraft.com/attachments/8Q3idkXQEG0WFz4AfSRQ_Rust%20Header%20Image%20512x256.png");
                SetSetting("server_identity", "my_very_own_server");

                SetSetting("server_automatic_restart", "0");
                SetSetting("server_automatic_restart_hour", "6");
                SetSetting("server_automatic_restart_minute", "0");

                SetSetting("server_last_wipe", DateTime.Now.ToString());

                SetSetting("server_ip", "0.0.0.0");
                SetSetting("server_port", "28015");
                SetSetting("server_queryport", "28014");
                SetSetting("server_appport", "28083");
                SetSetting("server_rconport", "28016");
                SetSetting("server_rconpassword", "your_rcon_password_" + rnd.Next(1, 1000000000));

                SetSetting("server_tags", "");
            }

            InitializeComponent();

            richTextBox1.LanguageOption =   RichTextBoxLanguageOptions.DualFont | 
                                            RichTextBoxLanguageOptions.UIFonts;

            timer1.Interval = 1000;
            timer1.Enabled = false;
            timer1.AutoReset = true;
            timer1.Elapsed += timer1_Tick;

            hide_timer.Interval = 100;
            hide_timer.Enabled = false;
            hide_timer.AutoReset = true;
            hide_timer.Elapsed += Hide_timer_Elapsed;

            wipe_timer.Interval = 60000;
            wipe_timer.Enabled = true;
            wipe_timer.AutoReset = true;
            wipe_timer.Elapsed += Wipe_timer_Elapsed;
            wipe_timer.Start();

            restart_timer.Interval = 60000;
            restart_timer.Enabled = false;
            restart_timer.AutoReset = false;
            restart_timer.Elapsed += Restart_timer_Elapsed;

            wipe_failed_timer.Interval = 300000;
            wipe_failed_timer.Enabled = false;
            wipe_failed_timer.AutoReset = false;
            wipe_failed_timer.Elapsed += Wipe_failed_timer_Elapsed;

            auto_wipe_timer.Interval = 60000;
            auto_wipe_timer.Enabled = false;
            auto_wipe_timer.AutoReset = false;
            auto_wipe_timer.Elapsed += Auto_wipe_timer_Elapsed;

            status_timer.Interval = 20000;
            status_timer.Enabled = false;
            status_timer.AutoReset = true;
            status_timer.Elapsed += Status_timer_Elapsed;
        }

        private async void Restart_timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            should_restart_full = true;
            SendRconCommand(@"say ""Reiniciando servidor...""");

            await Task.Delay(1000);

            SendRconCommand("quit");
            server_status_internal = InternalStatus.Stopping;
        }

        private async void Status_timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (ws.State == WebSocketState.Closed || ws.State == WebSocketState.Aborted)
            {
                ws = new ClientWebSocket();
                ws.ConnectAsync(new Uri("ws://localhost:" + GetSetting("server_rconport", "28016") + "/" + GetSetting("server_rconpassword")), CancellationToken.None);
                return;
            }

            //Log("> status");
            expect_status_packet = true;
            SendRconCommand("status", false);

            await Task.Delay(3000);

            //Check for new plugins as well
            expect_perms_packet = true;
            SendRconCommand("oxide.show perms", false);
        }

        private async void Auto_wipe_timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            SendRconCommand(@"say ""Wipando servidor...""");
            Log("Starting scheduled wipe...");
            wipe_timed_after_quit = true;

            await Task.Delay(2000);

            SendRconCommand("quit", true);

            server_status_internal = InternalStatus.Stopping;

        }

        private void Wipe_failed_timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            wipe_failed_timer.Stop();
            wipe_failed_timer.Enabled = false;
            wipe_failed_twice = true;

            AutoWipe(wipe_failed_type);
            wipe_failed_type = 0;
        }

        private void Wipe_timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            //Check for wipe schedules
            for(int i = 0; i < wipe_schedule_list.Count(); i++) 
            {
                //Match wipe time
                if (DateTime.Now.Hour == wipe_schedule_list[i].wipe_hour && DateTime.Now.Minute == wipe_schedule_list[i].wipe_minute)
                {
                    //Wipe every X days
                    if (wipe_schedule_list[i].wipe_schedule_type == 0)
                    {
                        DateTime now = DateTime.Now;
                        DateTime then = wipe_schedule_list[i].last_run;

                        now = now.AddSeconds(-(now.Second));
                        then = then.AddSeconds(-(then.Second));

                        //Check if last wipe was X days ago
                        if ((now - then).TotalDays >= wipe_schedule_list[i].xdays)
                        {
                            //Start timer and wipe after 5m.
                            wipe_schedule_list[i].last_run = DateTime.Now;

                            SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
                            LoadWipeScheduleList();

                            AutoWipe(wipe_schedule_list[i].wipe_type);
                            break;
                        }
                    }

                    //Wipe by weekday
                    if (wipe_schedule_list[i].wipe_schedule_type == 1)
                    {
                        //Check if today is the wipe weekday
                        if((int)DateTime.Now.DayOfWeek == wipe_schedule_list[i].weekday) 
                        {
                            //Start timer and wipe after 5m.
                            wipe_schedule_list[i].last_run = DateTime.Now;

                            SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
                            LoadWipeScheduleList();

                            AutoWipe(wipe_schedule_list[i].wipe_type);
                            break;
                        }
                    }

                    //Wipe by month day
                    if (wipe_schedule_list[i].wipe_schedule_type == 2)
                    {
                        //Check if today is the wipe month day
                        if (DateTime.Now.Day == wipe_schedule_list[i].monthday)
                        {
                            //Start timer and wipe after 5m.
                            wipe_schedule_list[i].last_run = DateTime.Now;

                            SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
                            LoadWipeScheduleList();

                            AutoWipe(wipe_schedule_list[i].wipe_type);
                            break;
                        }
                    }

                    //Wipe by 1st, 2nd, 3rd or last weekday of the month
                    if (wipe_schedule_list[i].wipe_schedule_type == 3)
                    {
                        if (wipe_schedule_list[i].first_weekday_of_month) 
                        {
                            DateTime first_of_month = DateTime.Now.NthOf(1, (DayOfWeek)wipe_schedule_list[i].weekday);
                            if (first_of_month.Day == DateTime.Now.Day)
                            {
                                wipe_schedule_list[i].last_run = DateTime.Now;

                                SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
                                LoadWipeScheduleList();

                                AutoWipe(wipe_schedule_list[i].wipe_type);
                                break;
                            }
                        }

                        if (wipe_schedule_list[i].second_weekday_of_month)
                        {
                            DateTime second_of_month = DateTime.Now.NthOf(2, (DayOfWeek)wipe_schedule_list[i].weekday);
                            if (second_of_month.Day == DateTime.Now.Day)
                            {
                                wipe_schedule_list[i].last_run = DateTime.Now;

                                SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
                                LoadWipeScheduleList();

                                AutoWipe(wipe_schedule_list[i].wipe_type);
                                break;
                            }
                        }

                        if (wipe_schedule_list[i].third_weekday_of_month)
                        {
                            DateTime third_of_month = DateTime.Now.NthOf(3, (DayOfWeek)wipe_schedule_list[i].weekday);
                            if (third_of_month.Day == DateTime.Now.Day)
                            {
                                wipe_schedule_list[i].last_run = DateTime.Now;

                                SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
                                LoadWipeScheduleList();

                                AutoWipe(wipe_schedule_list[i].wipe_type);
                                break;
                            }
                        }

                        if (wipe_schedule_list[i].last_weekday_of_month)
                        {
                            DateTime last_of_month = DateTime.Now.GetLastNDayInMonth((DayOfWeek)wipe_schedule_list[i].weekday);
                            if (last_of_month.Day == DateTime.Now.Day)
                            {
                                wipe_schedule_list[i].last_run = DateTime.Now;

                                SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
                                LoadWipeScheduleList();

                                AutoWipe(wipe_schedule_list[i].wipe_type);
                                break;
                            }
                        }
                    }
                }
            }

            //Check for message schedules
            if(server_status_internal == InternalStatus.Running) 
            {
                for (int i = 0; i < message_schedule_list.Count(); i++)
                {
                    //Match message time
                    if (DateTime.Now.Hour == message_schedule_list[i].message_hour && DateTime.Now.Minute == message_schedule_list[i].message_minute)
                    {
                        //Message every X days
                        if (message_schedule_list[i].message_schedule_type == 0)
                        {
                            DateTime now = DateTime.Now;
                            DateTime then = message_schedule_list[i].last_run;

                            now = now.AddSeconds(-(now.Second));
                            then = then.AddSeconds(-(then.Second));

                            //Check if last message was X days ago
                            if ((now - then).TotalDays >= message_schedule_list[i].xdays)
                            {
                                //Send message
                                message_schedule_list[i].last_run = DateTime.Now;

                                SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
                                LoadMessageScheduleList();

                                SendRconCommand("say " + message_schedule_list[i].message);

                                break;
                            }
                        }

                        //Message by weekday
                        if (message_schedule_list[i].message_schedule_type == 1)
                        {
                            //Check if today is the Message weekday
                            if ((int)DateTime.Now.DayOfWeek == message_schedule_list[i].weekday)
                            {
                                //Send message
                                message_schedule_list[i].last_run = DateTime.Now;

                                SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
                                LoadMessageScheduleList();

                                SendRconCommand("say " + message_schedule_list[i].message);

                                break;
                            }
                        }

                        //Message by month day
                        if (message_schedule_list[i].message_schedule_type == 2)
                        {
                            //Check if today is the message month day
                            if (DateTime.Now.Day == message_schedule_list[i].monthday)
                            {
                                //Send message
                                message_schedule_list[i].last_run = DateTime.Now;

                                SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
                                LoadMessageScheduleList();

                                SendRconCommand("say " + message_schedule_list[i].message);

                                break;
                            }
                        }

                        //Wipe by 1st, 2nd, 3rd or last weekday of the month
                        if (message_schedule_list[i].message_schedule_type == 3)
                        {
                            if (message_schedule_list[i].first_weekday_of_month)
                            {
                                DateTime first_of_month = DateTime.Now.NthOf(1, (DayOfWeek)message_schedule_list[i].weekday);
                                if (first_of_month.Day == DateTime.Now.Day)
                                {
                                    message_schedule_list[i].last_run = DateTime.Now;

                                    SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
                                    LoadMessageScheduleList();

                                    SendRconCommand("say " + message_schedule_list[i].message);

                                    break;
                                }
                            }

                            if (message_schedule_list[i].second_weekday_of_month)
                            {
                                DateTime second_of_month = DateTime.Now.NthOf(2, (DayOfWeek)message_schedule_list[i].weekday);
                                if (second_of_month.Day == DateTime.Now.Day)
                                {
                                    message_schedule_list[i].last_run = DateTime.Now;

                                    SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
                                    LoadMessageScheduleList();

                                    SendRconCommand("say " + message_schedule_list[i].message);

                                    break;
                                }
                            }

                            if (message_schedule_list[i].third_weekday_of_month)
                            {
                                DateTime third_of_month = DateTime.Now.NthOf(3, (DayOfWeek)message_schedule_list[i].weekday);
                                if (third_of_month.Day == DateTime.Now.Day)
                                {
                                    message_schedule_list[i].last_run = DateTime.Now;

                                    SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
                                    LoadMessageScheduleList();

                                    SendRconCommand("say " + message_schedule_list[i].message);

                                    break;
                                }
                            }

                            if (message_schedule_list[i].last_weekday_of_month)
                            {
                                DateTime last_of_month = DateTime.Now.GetLastNDayInMonth((DayOfWeek)message_schedule_list[i].weekday);
                                if (last_of_month.Day == DateTime.Now.Day)
                                {
                                    message_schedule_list[i].last_run = DateTime.Now;

                                    SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
                                    LoadMessageScheduleList();

                                    SendRconCommand("say " + message_schedule_list[i].message);

                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if(GetSetting("server_automatic_restart") == "1") 
            {
                if (DateTime.Now.Hour == int.Parse(GetSetting("server_automatic_restart_hour")) && DateTime.Now.Minute == int.Parse(GetSetting("server_automatic_restart_minute")))
                {
                    SendRconCommand(@"say ""O servidor será reiniciado em 1 minuto.""");
                    restart_timer.Enabled = true;
                    restart_timer.Start();
                }
            }

        }

        private void AutoWipe(int wipe_type = 0) 
        {
            if(server_status_internal == InternalStatus.Starting || server_status_internal == InternalStatus.Stopping) 
            {
                if (wipe_failed_twice) 
                {
                    Log("Scheduled wipe failed after two tries, wipe cancelled.");
                    wipe_failed_twice = false;
                    return;
                }
                Log("Scheduled wipe failed (server is starting or stopping), one last try will be made in 5 minutes.");
                wipe_failed_timer.Enabled = true;
                wipe_failed_timer.Start();
                wipe_failed_type = wipe_type;
                return;
            }

            bool start_server = server_status_internal == InternalStatus.Running;

            if (start_server) 
            {
                //Display message before wiping
                auto_wipe_timer.Enabled = true;
                auto_wipe_timer.Start();
                wipe_timed_type = wipe_type;
                Log("Server will be wiped in one minute!");
                SendRconCommand(@"say ""Servidor irá wipar em 1 minuto!""");
            }
            else
            {
                //Wipe now
                Log("Starting scheduled wipe...");
                switch (wipe_type)
                {
                    case 0:
                    default:
                        //Map wipe
                        Log("Wiping map...");
                        var ext_map = new List<string> { "map", "map.1", "map.2", "map.3", "map.4", "map.5", "sav", "sav.1", "sav.2", "sav.3", "sav.4", "sav.5" };
                        string[] dir_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/");

                        foreach (string map_file in dir_files)
                        {
                            if (ext_map.Any(x => map_file.EndsWith(x)))
                            {
                                Log("Deleting: " + map_file);
                                File.Delete(map_file);
                            }
                        }

                        Log("Map wipe complete!");
                        Log("     ");

                        this.Invoke(new MethodInvoker(delegate () {
                            textBox10.Text = ""+rnd.Next(1, int.MaxValue);
                            SetSetting("server_seed", "" + textBox10.Text);
                        }));

                        break;
                    case 1:
                        //Player wipe
                        Log("Wiping blueprints...");
                        string[] player_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.blueprints*");

                        foreach (string player_file in player_files)
                        {
                            Log("Deleting: " + player_file);
                            File.Delete(player_file);
                        }
                        Log("Blueprint wipe complete!");
                        Log("     ");

                        break;
                    case 2:
                        //Full wipe

                        Log("Wiping map...");
                        var full_ext_map = new List<string> { "map", "map.1", "map.2", "map.3", "map.4", "map.5", "sav", "sav.1", "sav.2", "sav.3", "sav.4", "sav.5" };
                        string[] full_dir_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/");

                        foreach (string map_file in full_dir_files)
                        {
                            if (full_ext_map.Any(x => map_file.EndsWith(x)))
                            {
                                Log("Deleting: " + map_file);
                                File.Delete(map_file);
                            }
                        }

                        Log("Wiping blueprints...");
                        string[] full_blueprint_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.blueprints*");

                        foreach (string player_file in full_blueprint_files)
                        {
                            Log("Deleting: " + player_file);
                            File.Delete(player_file);
                        }

                        Log("Wiping deaths...");
                        string[] full_deaths_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.deaths*");

                        foreach (string player_file in full_deaths_files)
                        {
                            Log("Deleting: " + player_file);
                            File.Delete(player_file);
                        }

                        Log("Wiping identities...");
                        string[] full_identities_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.identities*");

                        foreach (string player_file in full_identities_files)
                        {
                            Log("Deleting: " + player_file);
                            File.Delete(player_file);
                        }

                        Log("Wiping states...");
                        string[] full_states_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.states*");

                        foreach (string player_file in full_states_files)
                        {
                            Log("Deleting: " + player_file);
                            File.Delete(player_file);
                        }
                        Log("Full wipe complete!");
                        Log("     ");

                        this.Invoke(new MethodInvoker(delegate () {
                            textBox10.Text = ""+rnd.Next(1, int.MaxValue);
                            SetSetting("server_seed", "" + textBox10.Text);
                        }));

                        break;
                }
            }            
        }

        private void Hide_timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Process[] processlist = Process.GetProcesses();
            foreach (Process process in processlist)
            {
                if (!String.IsNullOrEmpty(process.MainWindowTitle))
                {
                    if (process.MainWindowTitle.ToLower().Contains("rustdedicated"))
                    {
                        if(hide_stop_timer == null) 
                        {
                            hide_stop_timer = new System.Timers.Timer();
                            hide_stop_timer.AutoReset = false;
                            hide_stop_timer.Enabled = true;
                            hide_stop_timer.Interval = 5000;
                            hide_stop_timer.Elapsed += Hide_stop_timer_Elapsed;
                            hide_stop_timer.Start();
                        }
                        IntPtr hWnd = process.MainWindowHandle;
                        int hWnd32 = process.MainWindowHandle.ToInt32();

                        long style = GetWindowLong(hWnd, -16);
                        style &= ~(0x10000000L);    // this works - window become invisible 

                        style |= 0x00000080L;   // flags don't work - windows remains in taskbar
                        style &= ~(0x00040000L);

                        ShowWindow(hWnd32, SW_HIDE); // hide the window
                        SetWindowLong(hWnd, -16, Convert.ToUInt32(style)); // set the style
                        ShowWindow(hWnd32, 5); // show the window for the new style to come into effect
                        ShowWindow(hWnd32, SW_HIDE); // hide the window so we can't see it

                        this.Invoke(new MethodInvoker(delegate () {
                            this.Activate();
                        }));
                        break;
                    }
                }
            }
        }

        private void Hide_stop_timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            hide_stop_timer.Stop();
            hide_stop_timer.Enabled = false;

            hide_timer.Stop();
            hide_timer.Enabled = false;
        }

        private void Form1_Load(object sender, EventArgs e)
        {

            if(Directory.Exists(Application.StartupPath + "/oxide"))
            {
                use_oxide = true;
            }

            wipe_schedule_list = JsonConvert.DeserializeObject<List<WipeSchedule>>(GetSetting("server_wipe_list", "[]"));
            message_schedule_list = JsonConvert.DeserializeObject<List<MessageSchedule>>(GetSetting("server_message_list", "[]"));
            LoadWipeScheduleList();
            LoadMessageScheduleList();

            textBox8.Text = GetSetting("steamcmd_path", "C:\\steamcmd\\steamcmd.exe");
            textBox1.Text = GetSetting("server_level", "Procedural Map");

            checkBox1.Checked = (GetSetting("server_automatic_restart", "0") == "1");
            numericUpDown3.Value = int.Parse(GetSetting("server_automatic_restart_hour", "6"));
            numericUpDown8.Value = int.Parse(GetSetting("server_automatic_restart_minute", "0"));

            try
            {
                textBox10.Text = GetSetting("server_seed", "" + rnd.Next(1, int.MaxValue));
            }
            catch { }

            try
            {
                textBox11.Text = GetSetting("server_seed_url", "");
            }
            catch { }

            try
            {
                numericUpDown1.Value = int.Parse(GetSetting("server_worldsize", "3000"));
            }
            catch { }

            try
            {
                numericUpDown2.Value = int.Parse(GetSetting("server_maxplayers", "75"));
            }
            catch { }

            label6.Text = "0/" + numericUpDown2.Value;

            textBox3.Text = GetSetting("server_hostname", "[REGION] YOUR SERVER NAME");
            richTextBox3.Text = GetSetting("server_description", "Your server description, good for rules.");
            textBox2.Text = GetSetting("server_url", "https://yourwebsite.com");
            textBox4.Text = GetSetting("server_headerimage", "https://s3-assets.nodecraft.com/attachments/8Q3idkXQEG0WFz4AfSRQ_Rust%20Header%20Image%20512x256.png");
            textBox5.Text = GetSetting("server_identity", "my_very_own_server");

            textBox6.Text = GetSetting("server_ip", "0.0.0.0");
            try
            {
                numericUpDown4.Value = int.Parse(GetSetting("server_port", "28015"));
            }
            catch { }

            try
            {
                numericUpDown5.Value = int.Parse(GetSetting("server_queryport", "28014"));
            }
            catch { }

            try
            {
                numericUpDown6.Value = int.Parse(GetSetting("server_appport", "28083"));
            }
            catch { }

            try
            {
                numericUpDown7.Value = int.Parse(GetSetting("server_rconport", "28016"));
            }
            catch { }

            textBox7.Text = GetSetting("server_rconpassword", "your_rcon_password_" + rnd.Next(1, 1000000000));

            textBox9.Text = GetSetting("server_tags", "");

            Log("Rust Server Manager successfully started.");
            Log("     ");

            string[] args = Environment.GetCommandLineArgs();

            foreach (string arg in args)
            {
                if (arg == "-restart")
                {
                    button2_Click(null, null);
                }
            }

        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://rustmaps.com/");
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            textBox10.Text = "" + rnd.Next(1, int.MaxValue);
            SetSetting("server_seed", "" + textBox10.Text);
        }

        private void linkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://imgur.com/");
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if(server_status_internal != InternalStatus.Stopped) 
            {
                MessageBox.Show("Rust server is already started or busy starting/stopping.", "Server already started or busy");
                return;
            }
            StartAndUpdate();
        }

        private void StartAndUpdate() 
        {
            server_status_internal = InternalStatus.Starting;
            try
            {
                File.WriteAllText(Application.StartupPath + "/server_out.log", "");
            }
            catch { }

            started = false;
            this.Invoke(new MethodInvoker(delegate () {
                tabControl1.SelectedTab = tabPage1;
            }));

            Log("Searching for server updates...");
            try
            {
                steamcmd_process.Kill();
            }
            catch { }

            try
            {
                rust_dedicated_process.Kill();
            }
            catch { }

            foreach (var process in Process.GetProcessesByName("RustDedicated"))
            {
                try
                {
                    process.Kill();
                }
                catch { }
            }

            foreach (var process in Process.GetProcessesByName("steamcmd"))
            {
                try
                {
                    process.Kill();
                }
                catch { }
            }

            steamcmd_process = new Process();
            rust_dedicated_process = new Process();

            ChangeServerStatus("Starting: check for updates");

            steamcmd_process.EnableRaisingEvents = true;
            steamcmd_process.OutputDataReceived += new System.Diagnostics.DataReceivedEventHandler(steamcmd_OutputDataReceived);
            steamcmd_process.ErrorDataReceived += new System.Diagnostics.DataReceivedEventHandler(steamcmd_OutputDataReceived);
            steamcmd_process.Exited += new System.EventHandler(steamcmd_Exited);

            steamcmd_process.StartInfo.FileName = textBox8.Text;
            steamcmd_process.StartInfo.WorkingDirectory = Application.StartupPath;
            steamcmd_process.StartInfo.Arguments = "+login anonymous +app_update 258550 +quit";
            steamcmd_process.StartInfo.UseShellExecute = false;
            steamcmd_process.StartInfo.RedirectStandardOutput = true;
            steamcmd_process.StartInfo.RedirectStandardError = true;
            steamcmd_process.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
            steamcmd_process.StartInfo.CreateNoWindow = true;

            steamcmd_process.Start();
            steamcmd_process.BeginOutputReadLine();
            steamcmd_process.BeginErrorReadLine();
        }


        void steamcmd_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if(e.Data != null && e.Data.Contains("Update state") && use_oxide) 
            {
                must_update_oxide = true;
            }
            Log(e.Data);
        }

        async void steamcmd_Exited(object sender, EventArgs e)
        {
            string old_hash = GetSetting("assembly_last_hash", "12345");
            string new_hash = "";

            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(Application.StartupPath + "\\RustDedicated_Data\\Managed\\Assembly-CSharp.dll"))
                {
                    new_hash = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLower();
                }
            }

            SetSetting("assembly_last_hash", new_hash);
            if (new_hash != old_hash) 
            {
                must_update_oxide = true;
            }

            if (must_update_oxide)
            {
                try
                {
                    Log("Downloading oxide update...");
                    ChangeServerStatus("Downloading new oxide update...");
                    using (var client = new WebClient())
                    {
                        await client.DownloadFileTaskAsync(new Uri("https://umod.org/games/rust/download?tag=public"), Application.StartupPath + "/oxide-update.zip");
                    }
                    Log("Oxide download complete, patching...");
                    ChangeServerStatus("Applying oxide update...");
                    await Task.Run(() =>
                    {
                        using (ZipArchive archive = ZipFile.OpenRead(Application.StartupPath + "/oxide-update.zip"))
                        {
                            archive.ExtractToDirectory(Application.StartupPath + "/", true);
                        }
                    });
                    File.Delete(Application.StartupPath + "/oxide-update.zip");
                }
                catch {
                    Log("Update patching failed, please try again.");
                }

                Log("Starting game server...");
                StartGameServer();
            }
            else
            {
                StartGameServer();
            }
        }

        void StartGameServer() 
        {
            server_status_internal = InternalStatus.Starting;
            ChangeServerStatus("Starting: loading game resources");

            rust_dedicated_process.EnableRaisingEvents = true;
            rust_dedicated_process.OutputDataReceived += new System.Diagnostics.DataReceivedEventHandler(rust_dedicated_OutputDataReceived);
            rust_dedicated_process.ErrorDataReceived += new System.Diagnostics.DataReceivedEventHandler(rust_dedicated_OutputDataReceived);
            rust_dedicated_process.Exited += new System.EventHandler(rust_dedicated_Exited);

            DateTime last_wipe_date = DateTime.Parse(GetSetting("server_last_wipe", DateTime.Now.ToString()));

            rust_dedicated_process.StartInfo.WorkingDirectory = Application.StartupPath;
            rust_dedicated_process.StartInfo.FileName = Application.StartupPath + "/RustDedicated.exe";
            this.Invoke(new MethodInvoker(delegate () {
                rust_dedicated_process.StartInfo.Arguments = "-batchmode " +
                    "-parentHWND '" + Process.GetCurrentProcess().MainWindowHandle.ToString() + "' delayed " +
                    "-popupwindow " +
                    "-screen-width 0 " +
                    "-screen-height 0 " +
                    "-screen-position-x -1500 " +
                    "-screen-position-y -1500 " +
                    "+server.level \"" + textBox1.Text + "\" " +
                    "+server.seed " + textBox10.Text + " " +
                    "+server.worldsize " + numericUpDown1.Value + " " +
                    "+server.maxplayers " + numericUpDown2.Value + " " +
                    "+server.hostname \"" + (textBox3.Text.Replace("{last_wipe_dm}", last_wipe_date.ToString("dd/MM")).Replace("{last_wipe_md}", last_wipe_date.ToString("MM/dd"))) + "\" " +
                    "+server.description \"" + FormatDescription(richTextBox3.Text) + "\" " +
                    "+server.url \"" + textBox2.Text + "\" " +
                    "+server.headerimage \"" + textBox4.Text + "\" " +
                    "+server.identity \"" + textBox5.Text + "\" " +
                    "+server.ip " + textBox6.Text + " " +
                    "+server.port " + numericUpDown4.Value + " " +
                    "+server.queryport " + numericUpDown5.Value + " " +
                    "+rcon.port " + numericUpDown7.Value + " " +
                    "+app.port " + numericUpDown6.Value + " " +
                    "+rcon.password \"" + textBox7.Text + "\" " +
                    "-logfile " + Application.StartupPath + "\\server_out.log";
            }));

            Log("    ");
            Log("Starting RUST server with arguments: ");
            Log(rust_dedicated_process.StartInfo.Arguments);
            Log("    ");

            rust_dedicated_process.StartInfo.UseShellExecute = false;
            rust_dedicated_process.StartInfo.RedirectStandardOutput = true;
            rust_dedicated_process.StartInfo.RedirectStandardError = true;
            rust_dedicated_process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            rust_dedicated_process.StartInfo.CreateNoWindow = true;

            rust_dedicated_process.Start();
            rust_dedicated_process.BeginOutputReadLine();
            rust_dedicated_process.BeginErrorReadLine();

            MoveWindow(rust_dedicated_process.MainWindowHandle, -5000, -5000, 10, 10, true);

            timer1.Enabled = true;
            timer1.Start();

            hide_timer.Enabled = true;
            //hide_timer.Start();
        }

        void rust_dedicated_Exited(object sender, EventArgs e)
        {
            server_status_internal = InternalStatus.Stopped;
            timer1.Stop();
            timer1.Enabled = false;
            Log("RustDedicated.exe closed.");
            ServerStopped();
            ChangeServerStatus("Stopped");

            if (should_restart_full) 
            {
                Log("Doing full restart.");

                Process restart_process = new Process();
                restart_process.StartInfo.WorkingDirectory = Application.StartupPath;
                restart_process.StartInfo.FileName = Process.GetCurrentProcess().MainModule.FileName;
                restart_process.StartInfo.Arguments = "-restart";
                restart_process.StartInfo.UseShellExecute = true;

                restart_process.Start();
                Process.GetCurrentProcess().Kill();
                return;
            }

            if (wipe_timed_after_quit) 
            {
                wipe_timed_after_quit = false;
                switch (wipe_timed_type)
                {
                    case 0:
                    default:
                        //Map wipe
                        Log("Wiping map...");
                        var ext_map = new List<string> { "map", "map.1", "map.2", "map.3", "map.4", "map.5", "sav", "sav.1", "sav.2", "sav.3", "sav.4", "sav.5" };
                        string[] dir_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/");

                        foreach (string map_file in dir_files)
                        {
                            if (ext_map.Any(x => map_file.EndsWith(x)))
                            {
                                Log("Deleting: " + map_file);
                                File.Delete(map_file);
                            }
                        }

                        Log("Map wipe complete!");
                        Log("     ");

                        if(textBox11.Text.Length > 0) 
                        {
                            //Get seed from web service
                            string new_seed = "";
                            using (var client = new WebClient())
                            {
                                new_seed = client.DownloadString(new Uri(textBox11.Text));
                            }

                            if (new_seed.Length > 0) 
                            {
                                this.Invoke(new MethodInvoker(delegate () {
                                    textBox10.Text = new_seed;
                                    SetSetting("server_seed", new_seed);
                                }));
                            }
                            else
                            {
                                this.Invoke(new MethodInvoker(delegate () {
                                    textBox10.Text = "" + rnd.Next(1, int.MaxValue);
                                    SetSetting("server_seed", "" + textBox10.Text);
                                }));
                            }
                        }
                        else
                        {
                            //Generate random seed
                            this.Invoke(new MethodInvoker(delegate () {
                                textBox10.Text = "" + rnd.Next(1, int.MaxValue);
                                SetSetting("server_seed", "" + textBox10.Text);
                            }));
                        }

                        break;
                    case 1:
                        //Player wipe
                        Log("Wiping blueprints...");
                        string[] player_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.blueprints*");

                        foreach (string player_file in player_files)
                        {
                            Log("Deleting: " + player_file);
                            File.Delete(player_file);
                        }
                        Log("Blueprint wipe complete!");
                        Log("     ");

                        break;
                    case 2:
                        //Full wipe

                        Log("Wiping map...");
                        var full_ext_map = new List<string> { "map", "map.1", "map.2", "map.3", "map.4", "map.5", "sav", "sav.1", "sav.2", "sav.3", "sav.4", "sav.5" };
                        string[] full_dir_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/");

                        foreach (string map_file in full_dir_files)
                        {
                            if (full_ext_map.Any(x => map_file.EndsWith(x)))
                            {
                                Log("Deleting: " + map_file);
                                File.Delete(map_file);
                            }
                        }

                        Log("Wiping blueprints...");
                        string[] full_blueprint_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.blueprints*");

                        foreach (string player_file in full_blueprint_files)
                        {
                            Log("Deleting: " + player_file);
                            File.Delete(player_file);
                        }

                        Log("Wiping deaths...");
                        string[] full_deaths_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.deaths*");

                        foreach (string player_file in full_deaths_files)
                        {
                            Log("Deleting: " + player_file);
                            File.Delete(player_file);
                        }

                        Log("Wiping identities...");
                        string[] full_identities_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.identities*");

                        foreach (string player_file in full_identities_files)
                        {
                            Log("Deleting: " + player_file);
                            File.Delete(player_file);
                        }

                        Log("Wiping states...");
                        string[] full_states_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.states*");

                        foreach (string player_file in full_states_files)
                        {
                            Log("Deleting: " + player_file);
                            File.Delete(player_file);
                        }
                        Log("Full wipe complete!");
                        Log("     ");

                        if (textBox11.Text.Length > 0)
                        {
                            //Get seed from web service
                            string new_seed = "";
                            using (var client = new WebClient())
                            {
                                new_seed = client.DownloadString(new Uri(textBox11.Text));
                            }

                            if (new_seed.Length > 0)
                            {
                                this.Invoke(new MethodInvoker(delegate () {
                                    textBox10.Text = new_seed;
                                    SetSetting("server_seed", new_seed);
                                }));
                            }
                            else
                            {
                                this.Invoke(new MethodInvoker(delegate () {
                                    textBox10.Text = "" + rnd.Next(1, int.MaxValue);
                                    SetSetting("server_seed", "" + textBox10.Text);
                                }));
                            }
                        }
                        else
                        {
                            //Generate random seed
                            this.Invoke(new MethodInvoker(delegate () {
                                textBox10.Text = "" + rnd.Next(1, int.MaxValue);
                                SetSetting("server_seed", "" + textBox10.Text);
                            }));
                        }

                        break;
                }
                wipe_timed_type = 0;
                StartAndUpdate();
            }

            if (wipe_map)
            {
                wipe_map = false;
                Wipe(0, true);
            }

            if (wipe_player)
            {
                wipe_player = false;
                Wipe(1, true);
            }

            if (wipe_full)
            {
                wipe_full = false;
                Wipe(2, true);
            }

            if (should_restart) 
            {
                should_restart = false;
                StartAndUpdate();
            }
        }

        void rust_dedicated_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Log(e.Data);
        }

        public string FormatDescription(string text) 
        {
            text = Regex.Replace(text, @"\r\n?|\n", "\\n");
            text = text.Replace("\"", "'");
            return text;
        }

        public void ChangeServerStatus(string newStatus)
        {
            this.Invoke(new MethodInvoker(delegate () {
                label3.Text = newStatus;
            }));
        }

        public void Log(string line)
        {
            if (line == null || line.Length <= 0 || line.Contains("(Filename:") || line.Contains(@"C:\buildslave\unity\build\Runtime/Export/Debug/Debug.bindings.h") || line.Contains("oxide.plugins") || line.StartsWith("Permissions:")) { return; }

            this.Invoke(new MethodInvoker(delegate () {

                if(richTextBox1.Lines.Length <= 0 || richTextBox1.Lines.Reverse().Skip(1).Take(1).First().Trim() != "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line.Trim()) 
                {
                    if (line.Trim().Length >= 2)
                    {
                        richTextBox1.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine);
                    }
                    else
                    {
                        //Probably just a line break
                        richTextBox1.AppendText(line + Environment.NewLine);
                    }

                    if (richTextBox1.Lines.Count() > 100)
                    {
                        richTextBox1.Lines = richTextBox1.Lines.Skip(Math.Abs(100 - richTextBox1.Lines.Count())).ToArray();
                    }

                    ScrollToBottom(richTextBox1);
                }
            }));
        }

        public void Log(string[] lines)
        {
            this.Invoke(new MethodInvoker(delegate () {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] == null || lines[i].Length <= 0 || lines[i].Contains("(Filename:") || lines[i].Contains(@"C:\buildslave\unity\build\Runtime/Export/Debug/Debug.bindings.h") || lines[i].Contains("oxide.plugins") || lines[i].StartsWith("Permissions:")) { continue; }

                    if (richTextBox1.Lines.Length <= 0 || richTextBox1.Lines.Reverse().Skip(1).Take(1).First().Trim() != "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + lines[i].Trim())
                    {
                        if (lines[i].Trim().Length >= 2) 
                        {
                            richTextBox1.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + lines[i] + Environment.NewLine);
                        }
                        else
                        {
                            //Probably just a line break
                            richTextBox1.AppendText(lines[i] + Environment.NewLine);
                        }
                    }
                }

                if (richTextBox1.Lines.Count() > 100)
                {
                    richTextBox1.Lines = richTextBox1.Lines.Skip(Math.Abs(100 - richTextBox1.Lines.Count())).ToArray();
                }

                ScrollToBottom(richTextBox1);
            }));
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            LogNewLines();
        }

        private async void ServerStarted() 
        {
            server_status_internal = InternalStatus.Running;
            ChangeServerStatus("Running");

            status_timer.Enabled = true;
            status_timer.Start();

            Log("Connecting to RCON Socket");

            ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri("ws://localhost:" + GetSetting("server_rconport", "28016") + "/" + GetSetting("server_rconpassword")), CancellationToken.None);

            SendRconCommand("server.tags " + textBox9.Text, false);
            await Task.Delay(1000);
            SendRconCommand("server.writecfg", false);
            await Task.Delay(1000);

            Status_timer_Elapsed(null, null);

            Listen();
        }

        private void ServerStopped() 
        {
            server_status_internal = InternalStatus.Stopped;
            ChangeServerStatus("Stopped");

            status_timer.Stop();
            status_timer.Enabled = false;

            try
            {
                ws.Abort();
            }
            catch { }
        }

        private async void Listen() 
        {
            if(ws.State == WebSocketState.Closed || ws.State == WebSocketState.Aborted) 
            {
                return;
            }

            try
            {
                ArraySegment<Byte> buffer = new ArraySegment<byte>(new Byte[8192]);
                WebSocketReceiveResult result;
                using (var ms = new MemoryStream())
                {
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                        ms.Write(buffer.Array, buffer.Offset, result.Count);
                    }
                    while (!result.EndOfMessage);

                    ms.Seek(0, SeekOrigin.Begin);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        using (var reader = new StreamReader(ms, Encoding.UTF8))
                        {
                            // do stuff
                            string data = reader.ReadToEnd();
                            Packet packet = JsonConvert.DeserializeObject<Packet>(data);

                            //Dont log empty messages
                            if (!data.Contains(@"""Message"": """"")) 
                            {
                                if (data.Contains(@"""Message"": ""hostname:"))
                                {
                                    //Received status update, updating dashboard info.
                                    UpdateDashboardInfo(packet);
                                    if (!expect_status_packet)
                                    {
                                        Log(data);
                                    }
                                    expect_status_packet = false;
                                }
                                else if (data.Contains(@"""Message"": ""Permissions:"))
                                {
                                    //Received status update, updating dashboard info.
                                    UpdatePluginInfo(packet);
                                    if (!expect_perms_packet)
                                    {
                                        Log(data);
                                    }
                                    expect_perms_packet = false;
                                }
                                else
                                {
                                    //Log only messages requested (dont log passive messages like player connect, disconnect, chat, etc)
                                    if(packet.Identifier > 0) 
                                    {
                                        Log(data);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e) { }
            Listen();
        }

        private void UpdatePluginInfo(Packet packet) 
        {
            string[] lines = packet.Message.Split(
                new string[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            );

            string[] perm_list = lines[1].Split(',');

            List<Plugin> updated_plugin_list = new List<Plugin>();
            foreach(string plugin in Directory.GetFiles(Application.StartupPath + "\\oxide\\plugins")) 
            {
                Plugin pl = new Plugin();
                pl.Filepath = plugin;
                pl.Name = plugin.Replace(Application.StartupPath + "\\oxide\\plugins\\", "");
                pl.Name = pl.Name.Replace(".cs", "");
                pl.Name = pl.Name.ToLower();

                string[] plugin_file_files = File.ReadAllLines(plugin);
                foreach(string plugin_file_line in plugin_file_files) 
                {
                    if (plugin_file_line.Contains("[Info("))
                    {
                        //INFO
                        string[] plugin_info = plugin_file_line.Replace("[Info(", "").Replace(")]", "").Replace("\"", "").Split(',');
                        pl.DisplayName = plugin_info[0].Trim();
                        pl.Author = plugin_info[1].Trim();
                        pl.Version = plugin_info[2].Trim();
                    }

                    if (plugin_file_line.Contains("[Description("))
                    {
                        //DESCRIPTION
                        pl.Description = plugin_file_line.Replace("[Description(", "").Replace(")]", "").Trim();
                    }
                }

                foreach (string perm in perm_list)
                {
                    if (perm.Trim().StartsWith(pl.Name))
                    {
                        pl.Permissions.Add(perm.Trim());
                    }
                }

                updated_plugin_list.Add(pl);
            }

            if(updated_plugin_list.Count() != plugin_list.Count()) 
            {
                plugin_list = updated_plugin_list;
                this.Invoke(new MethodInvoker(delegate () {
                    listBox2.Items.Clear();
                    foreach (Plugin pl in plugin_list)
                    {
                        listBox2.Items.Add("\"" + pl.DisplayName + "\" ver. " + pl.Version + " by " + pl.Author);
                    }
                    if (richTextBox4.Text == "Plugins will load shortly after the server startup.")
                    {
                        richTextBox4.Text = "";
                    }
                }));
            }
        }

        private void UpdateDashboardInfo(Packet packet) 
        {
            string[] lines = packet.Message.Split(
                new string[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            );

            try
            {
                this.Invoke(new MethodInvoker(delegate () {
                    Regex regex = new Regex(@"players : (\d{0,5}) \((\d{0,5}) max\) \((\d{0,5}) queued\) \((\d{0,5}) joining\)");
                    MatchCollection matches = regex.Matches(lines[3]);
                    label6.Text = matches[0].Groups[1].Value + "/" + matches[0].Groups[2].Value;
                    label7.Text = matches[0].Groups[4].Value;
                    label26.Text = matches[0].Groups[3].Value;

                    //Check for players
                    listView1.Items.Clear();

                    if (lines.Length >= 8) 
                    {
                        Regex regex2 = new Regex("(\\d+)\\s+\\\"(.+)\\\"\\s+(\\d+)\\s+(\\d+)\\.(\\d+)s\\s+(\\d+).(\\d+).(\\d+).(\\d+):(\\d+)");

                        for (int i = 6; i < lines.Length; i++)
                        {
                            MatchCollection matches2 = regex2.Matches(lines[i]);

                            if(matches2.Count > 0 && matches2[0].Groups.Count >= 11) 
                            {
                                ListViewItem item1 = new ListViewItem(matches2[0].Groups[2].Value);
                                item1.SubItems.Add(matches2[0].Groups[1].Value);
                                item1.SubItems.Add(matches2[0].Groups[6].Value + "." + matches2[0].Groups[7].Value + "." + matches2[0].Groups[8].Value + "." + matches2[0].Groups[9].Value + ":" + matches2[0].Groups[10].Value);
                                item1.SubItems.Add(matches2[0].Groups[3].Value);

                                listView1.Items.Add(item1);
                            }
                        }
                    }
                }));
            }
            catch { }
        }

        private async void SendRconCommand(string cmd, bool log = true) 
        {
            if(server_status_internal != InternalStatus.Running) 
            {
                return;
            }

            if (log) { Log("> " + cmd); }

            if (ws == null || ws.State == WebSocketState.Closed || ws.State == WebSocketState.Aborted)
            {
                Log("Socket is closed, trying to start...");
                ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri("ws://localhost:" + GetSetting("server_rconport", "28016") + "/" + GetSetting("server_rconpassword")), CancellationToken.None);
            }

            Packet packet = new Packet();
            packet.Identifier = 1234;
            packet.Message = cmd;
            packet.Name = "RustServerManager";

            // Send the connect request and wait for the response
            try{
                var sendBuffer = new ArraySegment<Byte>(Encoding.UTF8.GetBytes(Regex.Replace(JsonConvert.SerializeObject(packet), "([^\"]+)\":/g", "$1:")));
                await ws.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch {  }
        }

        private void LogNewLines() 
        {
            try
            {
                string[] lines = WriteSafeReadAllLines(Application.StartupPath + "\\server_out.log");

                if( (lines.ToList().Contains("Server startup complete") || lines.ToList().Contains("SteamServer Connected") ) && !started) 
                {
                    started = true;
                    ServerStarted();
                }

                Log(lines.ToList().Skip(last_log_line + 1).ToArray());

                last_log_line = lines.Length;

                //Clear log when over 2000 lines
                if(last_log_line >= 2000) 
                {
                    //File.WriteAllText(Application.StartupPath + "/server_out.log", "");
                    using (FileStream fs = new FileStream(Application.StartupPath + "/server_out.log", FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) 
                    {
                        fs.SetLength(0);
                    }
                    last_log_line = 0;
                }
            }
            catch { }
        }

        public string[] WriteSafeReadAllLines(String path)
        {
            using (var csv = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(csv))
            {
                List<string> file = new List<string>();
                while (!sr.EndOfStream)
                {
                    file.Add(sr.ReadLine());
                }

                return file.ToArray();
            }
        }

        bool canClose = false;
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!canClose) 
            {
                canClose = true;
                e.Cancel = true;
                try
                {
                    steamcmd_process.Kill();
                }
                catch { }

                try
                {
                    rust_dedicated_process.Kill();
                }
                catch { }

                Application.Exit();
            }
        }

        static string GetSetting(string key, string defaultValue = "")
        {
            string result = defaultValue;
            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                result = appSettings[key] ?? defaultValue;
            }
            catch (ConfigurationErrorsException)
            {

            }
            return result;
        }

        static void SetSetting(string key, string value)
        {
            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if (settings[key] == null)
                {
                    settings.Add(key, value);
                }
                else
                {
                    settings[key].Value = value;
                }
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
            catch (ConfigurationErrorsException)
            {

            }
        }

        private void applySettings(object sender, EventArgs e)
        {
            SetSetting("steamcmd_path", textBox8.Text);
            SetSetting("server_level", textBox1.Text);
            SetSetting("server_seed", "" + textBox10.Text);
            SetSetting("server_seed_url", textBox11.Text);
            SetSetting("server_worldsize", "" + numericUpDown1.Value);
            SetSetting("server_maxplayers", "" + numericUpDown2.Value);
            SetSetting("server_hostname", textBox3.Text);
            SetSetting("server_description", richTextBox3.Text);
            SetSetting("server_url", textBox2.Text);
            SetSetting("server_headerimage", textBox4.Text);
            SetSetting("server_identity", textBox5.Text);

            SetSetting("server_ip", textBox6.Text);
            SetSetting("server_port", "" + numericUpDown4.Value);
            SetSetting("server_queryport", "" + numericUpDown5.Value);
            SetSetting("server_appport", "" + numericUpDown6.Value);
            SetSetting("server_rconport", "" + numericUpDown7.Value);
            SetSetting("server_rconpassword", textBox7.Text);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            SendRconCommand(console_textbox.Text);
            console_textbox.Text = "";
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if(server_status_internal != InternalStatus.Running) 
            {
                MessageBox.Show("Rust server is already stopped or busy starting/stopping.", "Server already stopped or busy");
                return;
            }
            SendRconCommand("quit");
            server_status_internal = InternalStatus.Stopping;

            this.Invoke(new MethodInvoker(delegate () {
                tabControl1.SelectedTab = tabPage1;
            }));

        }

        public void CreateMessageEveryXDays(int day_count = 5, int hour = 17, int minute = 0, string message = "")
        {
            MessageSchedule ms = new MessageSchedule();
            ms.message_schedule_type = 0;
            ms.message_hour = hour;
            ms.message_minute = minute;
            ms.last_run = DateTime.Now;
            ms.message = message;

            ms.xdays = day_count;

            message_schedule_list.Add(ms);

            SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
            LoadMessageScheduleList();
        }


        public void CreateWipeEveryXDays(int day_count = 5, int hour = 17, int minute = 0, int wipe_type = 0)
        {
            WipeSchedule wp = new WipeSchedule();
            wp.wipe_type = wipe_type;
            wp.wipe_schedule_type = 0;
            wp.wipe_hour = hour;
            wp.wipe_minute = minute;
            wp.last_run = DateTime.Now;

            wp.xdays = day_count;

            wipe_schedule_list.Add(wp);

            SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
            LoadWipeScheduleList();
        }

        public void CreateMessageEveryWeekday(int weekday = 4, int hour = 17, int minute = 0, string message = "")
        {
            MessageSchedule ms = new MessageSchedule();
            ms.message_schedule_type = 1;
            ms.message_hour = hour;
            ms.message_minute = minute;
            ms.last_run = DateTime.Now;
            ms.message = message;

            ms.weekday = weekday;

            message_schedule_list.Add(ms);

            SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
            LoadMessageScheduleList();
        }

        public void CreateWipeEveryWeekday(int weekday = 4, int hour = 17, int minute = 0, int wipe_type = 0)
        {
            WipeSchedule wp = new WipeSchedule();
            wp.wipe_type = wipe_type;
            wp.wipe_schedule_type = 1;
            wp.wipe_hour = hour;
            wp.wipe_minute = minute;
            wp.last_run = DateTime.Now;

            wp.weekday = weekday;

            wipe_schedule_list.Add(wp);

            SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
            LoadWipeScheduleList();
        }

        public void CreateMessageEveryMonthday(int monthday = 5, int hour = 17, int minute = 0, string message = "")
        {
            MessageSchedule ms = new MessageSchedule();
            ms.message_schedule_type = 2;
            ms.message_hour = hour;
            ms.message_minute = minute;
            ms.last_run = DateTime.Now;
            ms.message = message;

            ms.monthday = monthday;

            message_schedule_list.Add(ms);

            SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
            LoadMessageScheduleList();
        }

        public void CreateWipeEveryMonthday(int monthday = 5, int hour = 17, int minute = 0, int wipe_type = 0)
        {
            WipeSchedule wp = new WipeSchedule();
            wp.wipe_type = wipe_type;
            wp.wipe_schedule_type = 2;
            wp.wipe_hour = hour;
            wp.wipe_minute = minute;
            wp.last_run = DateTime.Now;

            wp.monthday = monthday;

            wipe_schedule_list.Add(wp);

            SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
            LoadWipeScheduleList();
        }

        public void CreateMessageEvery1st2nd3rd(int weekday = 4, bool first = true, bool second = false, bool third = false, bool last = false, int hour = 17, int minute = 0, string message = "")
        {
            MessageSchedule ms = new MessageSchedule();
            ms.message_schedule_type = 3;
            ms.message_hour = hour;
            ms.message_minute = minute;
            ms.last_run = DateTime.Now;
            ms.message = message;

            ms.weekday = weekday;

            ms.first_weekday_of_month = first;
            ms.second_weekday_of_month = second;
            ms.third_weekday_of_month = third;
            ms.last_weekday_of_month = last;

            message_schedule_list.Add(ms);

            SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
            LoadMessageScheduleList();
        }

        public void CreateWipeEvery1st2nd3rd(int weekday = 4, bool first = true, bool second = false, bool third = false, bool last = false, int hour = 17, int minute = 0, int wipe_type = 0)
        {
            WipeSchedule wp = new WipeSchedule();
            wp.wipe_type = wipe_type;
            wp.wipe_schedule_type = 3;
            wp.wipe_hour = hour;
            wp.wipe_minute = minute;
            wp.last_run = DateTime.Now;

            wp.weekday = weekday;

            wp.first_weekday_of_month = first;
            wp.second_weekday_of_month = second;
            wp.third_weekday_of_month = third;
            wp.last_weekday_of_month = last;

            wipe_schedule_list.Add(wp);

            SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
            LoadWipeScheduleList();
        }

        private void LoadMessageScheduleList()
        {
            this.Invoke(new MethodInvoker(delegate () {
                listBox3.Items.Clear();

                for (int i = 0; i < message_schedule_list.Count(); i++)
                {
                    switch (message_schedule_list[i].message_schedule_type)
                    {
                        case 0:
                        default:
                            listBox3.Items.Add(i + ": '" + message_schedule_list[i].message + "' every " + message_schedule_list[i].xdays + " days at " + message_schedule_list[i].message_hour.ToString().PadLeft(2, '0') + ":" + message_schedule_list[i].message_minute.ToString().PadLeft(2, '0'));
                            break;
                        case 1:
                            listBox3.Items.Add(i + ": '" + message_schedule_list[i].message + "' every " + Enum.GetName(typeof(DayOfWeek), message_schedule_list[i].weekday) + " at " + message_schedule_list[i].message_hour.ToString().PadLeft(2, '0') + ":" + message_schedule_list[i].message_minute.ToString().PadLeft(2, '0'));
                            break;
                        case 2:
                            listBox3.Items.Add(i + ": '" + message_schedule_list[i].message + "' every month at day " + message_schedule_list[i].monthday + " at " + message_schedule_list[i].message_hour.ToString().PadLeft(2, '0') + ":" + message_schedule_list[i].message_minute.ToString().PadLeft(2, '0'));
                            break;
                        case 3:
                            if (message_schedule_list[i].first_weekday_of_month)
                            {
                                listBox3.Items.Add(i + ": '" + message_schedule_list[i].message + "' every first " + Enum.GetName(typeof(DayOfWeek), message_schedule_list[i].weekday) + " of the month at " + message_schedule_list[i].message_hour.ToString().PadLeft(2, '0') + ":" + message_schedule_list[i].message_minute.ToString().PadLeft(2, '0'));
                            }
                            else if (message_schedule_list[i].second_weekday_of_month)
                            {
                                listBox3.Items.Add(i + ": '" + message_schedule_list[i].message + "' every second " + Enum.GetName(typeof(DayOfWeek), message_schedule_list[i].weekday) + " of the month at " + message_schedule_list[i].message_hour.ToString().PadLeft(2, '0') + ":" + message_schedule_list[i].message_minute.ToString().PadLeft(2, '0'));
                            }
                            else if (message_schedule_list[i].third_weekday_of_month)
                            {
                                listBox3.Items.Add(i + ": '" + message_schedule_list[i].message + "' every third " + Enum.GetName(typeof(DayOfWeek), message_schedule_list[i].weekday) + " of the month at " + message_schedule_list[i].message_hour.ToString().PadLeft(2, '0') + ":" + message_schedule_list[i].message_minute.ToString().PadLeft(2, '0'));
                            }
                            else if (message_schedule_list[i].last_weekday_of_month)
                            {
                                listBox3.Items.Add(i + ": '" + message_schedule_list[i].message + "' every last " + Enum.GetName(typeof(DayOfWeek), message_schedule_list[i].weekday) + " of the month at " + message_schedule_list[i].message_hour.ToString().PadLeft(2, '0') + ":" + message_schedule_list[i].message_minute.ToString().PadLeft(2, '0'));
                            }
                            break;
                    }
                }
            }));
        }

        private void LoadWipeScheduleList() 
        {
            this.Invoke(new MethodInvoker(delegate () {
                listBox1.Items.Clear();

                for(int i = 0; i < wipe_schedule_list.Count(); i++) 
                {
                    switch (wipe_schedule_list[i].wipe_schedule_type) 
                    {
                        case 0: default:
                            listBox1.Items.Add(i + ": " + wipe_type_names[wipe_schedule_list[i].wipe_type] + " wipe every " + wipe_schedule_list[i].xdays + " days at " + wipe_schedule_list[i].wipe_hour.ToString().PadLeft(2, '0') + ":" + wipe_schedule_list[i].wipe_minute.ToString().PadLeft(2, '0'));
                            break;
                        case 1:
                            listBox1.Items.Add(i + ": " + wipe_type_names[wipe_schedule_list[i].wipe_type] + " wipe every " + Enum.GetName(typeof(DayOfWeek), wipe_schedule_list[i].weekday) + " at " + wipe_schedule_list[i].wipe_hour.ToString().PadLeft(2, '0') + ":" + wipe_schedule_list[i].wipe_minute.ToString().PadLeft(2, '0'));
                            break;
                        case 2:
                            listBox1.Items.Add(i + ": " + wipe_type_names[wipe_schedule_list[i].wipe_type] + " wipe every month at day " + wipe_schedule_list[i].monthday + " at " + wipe_schedule_list[i].wipe_hour.ToString().PadLeft(2, '0') + ":" + wipe_schedule_list[i].wipe_minute.ToString().PadLeft(2, '0'));
                            break;
                        case 3:
                            if (wipe_schedule_list[i].first_weekday_of_month) 
                            {
                                listBox1.Items.Add(i + ": " + wipe_type_names[wipe_schedule_list[i].wipe_type] + " wipe every first " + Enum.GetName(typeof(DayOfWeek), wipe_schedule_list[i].weekday) + " of the month at " + wipe_schedule_list[i].wipe_hour.ToString().PadLeft(2, '0') + ":" + wipe_schedule_list[i].wipe_minute.ToString().PadLeft(2, '0'));
                            }
                            else if (wipe_schedule_list[i].second_weekday_of_month)
                            {
                                listBox1.Items.Add(i + ": " + wipe_type_names[wipe_schedule_list[i].wipe_type] + " wipe every second " + Enum.GetName(typeof(DayOfWeek), wipe_schedule_list[i].weekday) + " of the month at " + wipe_schedule_list[i].wipe_hour.ToString().PadLeft(2, '0') + ":" + wipe_schedule_list[i].wipe_minute.ToString().PadLeft(2, '0'));
                            }
                            else if (wipe_schedule_list[i].third_weekday_of_month) 
                            {
                                listBox1.Items.Add(i + ": " + wipe_type_names[wipe_schedule_list[i].wipe_type] + " wipe every third " + Enum.GetName(typeof(DayOfWeek), wipe_schedule_list[i].weekday) + " of the month at " + wipe_schedule_list[i].wipe_hour.ToString().PadLeft(2, '0') + ":" + wipe_schedule_list[i].wipe_minute.ToString().PadLeft(2, '0'));
                            }
                            else if (wipe_schedule_list[i].last_weekday_of_month)
                            {
                                listBox1.Items.Add(i + ": " + wipe_type_names[wipe_schedule_list[i].wipe_type] + " wipe every last " + Enum.GetName(typeof(DayOfWeek), wipe_schedule_list[i].weekday) + " of the month at " + wipe_schedule_list[i].wipe_hour.ToString().PadLeft(2, '0') + ":" + wipe_schedule_list[i].wipe_minute.ToString().PadLeft(2, '0'));
                            }
                            break;
                    }
                }
            }));
        }

        private void Wipe(int wipe_type = 0, bool start_server = false) 
        {
            Log("Wiping server...");
            switch (wipe_type)
            {
                case 0: default:
                    //Map wipe
                    Log("Wiping map...");
                    var ext_map = new List<string> { "map", "map.1", "map.2", "map.3", "map.4", "map.5", "sav", "sav.1", "sav.2", "sav.3", "sav.4", "sav.5" };
                    string[] dir_files  = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/");

                    foreach (string map_file in dir_files) 
                    {
                        if(ext_map.Any(x => map_file.EndsWith(x))) 
                        {
                            Log("Deleting: " + map_file);
                            File.Delete(map_file);
                        }
                    }
                    Log("Map wipe complete!");
                    Log("     ");

                    DialogResult dialogResult = MessageBox.Show("Map wipe completed, do you want to generate a new random seed for the new map?", "Map wipe completed", MessageBoxButtons.YesNo);
                    if (dialogResult == DialogResult.Yes)
                    {
                        this.Invoke(new MethodInvoker(delegate () {
                            textBox10.Text = ""+rnd.Next(1, int.MaxValue);
                            SetSetting("server_seed", "" + textBox10.Text);
                        }));
                    }

                    break;
                case 1:
                    //Player wipe
                    Log("Wiping blueprints...");
                    string[] player_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.blueprints*");

                    foreach (string player_file in player_files)
                    {
                        Log("Deleting: " + player_file);
                        File.Delete(player_file);
                    }
                    Log("Blueprint wipe complete!");
                    Log("     ");

                    break;
                case 2:
                    //Full wipe

                    Log("Wiping map...");
                    var full_ext_map = new List<string> { "map", "map.1", "map.2", "map.3", "map.4", "map.5", "sav", "sav.1", "sav.2", "sav.3", "sav.4", "sav.5" };
                    string[] full_dir_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/");

                    foreach (string map_file in full_dir_files)
                    {
                        if (full_ext_map.Any(x => map_file.EndsWith(x)))
                        {
                            Log("Deleting: " + map_file);
                            File.Delete(map_file);
                        }
                    }

                    Log("Wiping blueprints...");
                    string[] full_blueprint_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.blueprints*");

                    foreach (string player_file in full_blueprint_files)
                    {
                        Log("Deleting: " + player_file);
                        File.Delete(player_file);
                    }

                    Log("Wiping deaths...");
                    string[] full_deaths_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.deaths*");

                    foreach (string player_file in full_deaths_files)
                    {
                        Log("Deleting: " + player_file);
                        File.Delete(player_file);
                    }

                    Log("Wiping identities...");
                    string[] full_identities_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.identities*");

                    foreach (string player_file in full_identities_files)
                    {
                        Log("Deleting: " + player_file);
                        File.Delete(player_file);
                    }

                    Log("Wiping states...");
                    string[] full_states_files = Directory.GetFiles(Application.StartupPath + "/server/" + GetSetting("server_identity") + "/", "player.states*");

                    foreach (string player_file in full_states_files)
                    {
                        Log("Deleting: " + player_file);
                        File.Delete(player_file);
                    }
                    Log("Full wipe complete!");
                    Log("     ");

                    DialogResult full_dialogResult = MessageBox.Show("Full wipe completed, do you want to generate a new random seed for the new map?", "Full wipe completed", MessageBoxButtons.YesNo);
                    if (full_dialogResult == DialogResult.Yes)
                    {
                        this.Invoke(new MethodInvoker(delegate () {
                            textBox10.Text = ""+rnd.Next(1, int.MaxValue);
                            SetSetting("server_seed", "" + textBox10.Text);
                        }));
                    }

                    break;
            }
            if (start_server) { StartAndUpdate(); }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            //Map wipe
            DialogResult dialogResult = MessageBox.Show("Map wipe will stop the server, wipe all the map and start the server again, keeping players blueprints.", "Are you sure?", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                tabControl1.SelectedTab = tabPage1;

                Process[] pname = Process.GetProcessesByName("RustDedicated");
                if (pname.Length > 0) 
                {
                    //Send quit command and wipe after its done
                    wipe_map = true;
                    Log("Stopping server for wipe...");
                    SendRconCommand("quit");
                    server_status_internal = InternalStatus.Stopping;
                }
                else
                {
                    //Wipe now
                    Wipe(0);
                }
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            //Player wipe
            DialogResult dialogResult = MessageBox.Show("Player wipe will stop the server, wipe all player data (blueprints and inventory), and start the server again, keeping the same map.", "Are you sure?", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                tabControl1.SelectedTab = tabPage1;

                Process[] pname = Process.GetProcessesByName("RustDedicated");
                if (pname.Length > 0)
                {
                    //Send quit command and wipe after its done
                    wipe_player = true;
                    Log("Stopping server for wipe...");
                    SendRconCommand("quit");
                    server_status_internal = InternalStatus.Stopping;
                }
                else
                {
                    //Wipe now
                    Wipe(1);
                }
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            //Full wipe
            DialogResult dialogResult = MessageBox.Show("Full wipe will stop the server, delete all player and map data, and start the server again, keeping nothing.", "Are you sure?", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                tabControl1.SelectedTab = tabPage1;

                Process[] pname = Process.GetProcessesByName("RustDedicated");
                if (pname.Length > 0)
                {
                    //Send quit command and wipe after its done
                    wipe_full = true;
                    Log("Stopping server for wipe...");
                    SendRconCommand("quit");
                    server_status_internal = InternalStatus.Stopping;
                }
                else
                {
                    //Wipe now
                    Wipe(2);
                }
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            if(server_status_internal == InternalStatus.Stopped) 
            {
                StartAndUpdate();
                return;
            }

            if (server_status_internal == InternalStatus.Running)
            {
                should_restart = true;
                SendRconCommand("quit");
                server_status_internal = InternalStatus.Stopping;

                this.Invoke(new MethodInvoker(delegate () {
                    tabControl1.SelectedTab = tabPage1;
                }));

                return;
            }

            MessageBox.Show("Rust server is busy starting/stopping.", "Server is busy");
        }

        private void button8_Click(object sender, EventArgs e)
        {
            AddWipeSchedule add_schedule = new AddWipeSchedule(this);
            add_schedule.ShowDialog();
        }

        private void button10_Click(object sender, EventArgs e)
        {
            if(listBox1.SelectedIndex < 0) { return; }
            wipe_schedule_list.RemoveAt(listBox1.SelectedIndex);
            SetSetting("server_wipe_list", JsonConvert.SerializeObject(wipe_schedule_list));
            LoadWipeScheduleList();
        }

        private void linkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://umod.org/plugins?categories=rust");
        }

        private void button9_Click(object sender, EventArgs e)
        {
            if(listBox2.SelectedIndex < 0) { return; }
            DialogResult dialogResult = MessageBox.Show("Do you really want to remove this plugin?", "Remove plugin", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                File.Delete(plugin_list[listBox2.SelectedIndex].Filepath);
            }
        }


        private void button11_Click(object sender, EventArgs e)
        {
            if (listBox2.SelectedIndex < 0) { return; }
            System.Diagnostics.Process.Start("https://umod.org/plugins/" + plugin_list[listBox2.SelectedIndex].DisplayName.Replace(" ", "-").ToLower());
        }

        private void listBox2_SelectedValueChanged(object sender, EventArgs e)
        {
            ShowPluginInfo(listBox2.SelectedIndex);
        }

        private void ShowPluginInfo(int index) 
        {
            if(index < 0 || index >= plugin_list.Count()) { return; }
            richTextBox4.Text = "";

            List<string> lines = new List<string>();

            lines.Add("'" + plugin_list[index].DisplayName + "' version " + plugin_list[index].Version);
            lines.Add("Author: " + plugin_list[index].Author);
            lines.Add(plugin_list[index].Description);
            lines.Add("");
            lines.Add("Permissions: ");
            foreach(string perm in plugin_list[index].Permissions) 
            {
                lines.Add(" - " + perm);
            }

            richTextBox4.Lines = lines.ToArray();
        }

        private void console_textbox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                SendRconCommand(console_textbox.Text);
                console_textbox.Text = "";

                e.Handled = true;
            }
        }

        private void listBox2_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void linkLabel5_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            TagsForm tf = new TagsForm(this, GetSetting("server_tags"));
            tf.ShowDialog();
        }

        public void SaveTags(string tags) 
        {
            SetSetting("server_tags", tags);
            textBox9.Text = tags;
        }

        private void button12_Click(object sender, EventArgs e)
        {
            AddAutoMessage add_auto_message = new AddAutoMessage(this);
            add_auto_message.ShowDialog();
        }

        private void button13_Click(object sender, EventArgs e)
        {
            if (listBox3.SelectedIndex < 0) { return; }
            message_schedule_list.RemoveAt(listBox3.SelectedIndex);
            SetSetting("server_message_list", JsonConvert.SerializeObject(message_schedule_list));
            LoadMessageScheduleList();

        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked) 
            {
                SetSetting("server_automatic_restart", "1");
            }
            else {
                SetSetting("server_automatic_restart", "0");
            }
        }

        private void numericUpDown3_Leave(object sender, EventArgs e)
        {
            SetSetting("server_automatic_restart_hour", ""+numericUpDown3.Value);
        }

        private void numericUpDown8_Leave(object sender, EventArgs e)
        {
            SetSetting("server_automatic_restart_minute", "" + numericUpDown8.Value);
        }
    }
}