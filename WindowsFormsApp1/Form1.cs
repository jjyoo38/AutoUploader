using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Xml;
using System.Linq;
using ICSharpCode.SharpZipLib.Zip;
using System.Net.Sockets;
using Microsoft.Win32;
using System.Drawing;

namespace AutoUploader
{
    public partial class Form1 : Form
    {
        // 2020년 07월 04일 
        // 제작자 : 유재준
        // 운영직원들이여 힘냅시다..     
        //
        // 프로세스중에 PPC라는 단어가 포함된 프로세스를 찾아서 PPC[0]에 파일명을, PPCPath[0]에 경로를 입력함
        // PPCPath[0] 경로의 config 파일에서 PCCode를 찾아서 FTP 서버에 해당 이름으로 폴더를 만들고 
        // 그 안에 PCCode명.zip 파일로 업로드 함. (업로드 timer는 매일 새벽 2시, 자동 업데이트는 새벽 4시) 
        // 그리고 ExtraFileName 배열에 있는 파일명 목록이 프로세스에 있는지 추가로 확인하고, 만약 존재하면
        // 해당 이름으로 압축하여 FTP 서버에 올림. 마지막으로, 시작 프로그램 레지스트리를 확인하여 프로그램이 없으면
        // 프로그램 경로를 추가함.. ZIP 라이브러리는 .net 4.5에 기본 포함되어있으나, 아산공장은 대부분 .net 3.5~4가 
        // 설치 되어 있으므로 SharpZipLib이라는 DLL 라이브러리를 추가로 사용하였음
        //
        // ※ 자동 업데이트 프로세스 : FTP 폴더/0.Update/AutoUploader.exe 파일의 타임스탬프를 가져와서 
        // 로컬의 파일 날짜와 비교한 후 FTP에 있는 파일이 더 최신인 경우 CreateUpdateBatch 함수를 불러와서
        // 자체적으로 배치파일을 만들어 현재 프로세스를 종료하고 배치파일에서 업데이트 후 다시 현재 프로세스를 시작함
        //
        ///////////// PPC에서 같이 실행되는 프로그램 리스트 입력(대소문자 구분 안함) ////////////////////
        string[] ExtraFileNames = new string[] { "BarCodePrint", "DpsLamp", "DpsRackManager", "MonTool" };
        /////////////////////////////////////////////////////////////////////////////////////////////////

        string PPCfilename = "PPC";
        string[] PPC;
        string[] PPCPath;
        string TempDir = Path.GetTempPath();
        string TempFile = string.Empty;
        string config = "config.ini";
        string zipname = string.Empty;
        string subfile = string.Empty;
        string subpath = string.Empty;
        string PCCode = string.Empty;
        string PCCodeDir = string.Empty;
        string batchPath = Path.GetTempPath() + @"\Update.cmd";
       
        // FTP 서버 접속 설정 
        string id = "kfa2";
        string pw = "mobis/1251";
        string IP = "10.243.151.18";
        int port = 21;

        // 타이머 관련
        int Uploadhour = 4;
        int Updatehour = 2;
        string Timeformat = "yyyy-MM-dd HH:mm:ss";
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        

        public Form1()
        {
            InitializeComponent();

            this.WindowState = FormWindowState.Minimized;
            this.Visible = false;
            this.ShowInTaskbar = false;
            notifyIcon1.ContextMenuStrip = contextMenuStrip1;
        }
        
        private void Form1_Load(object sender, EventArgs e)
        {
            PCCODELABEL.Text = string.Empty;
            runningppclabel.Text = string.Empty;
            ppcpathlabel.Text = string.Empty;
            string O1 = string.Empty;
            string O2 = string.Empty;

            timer.Interval = 60 * 1000;
            timer.Tick += new EventHandler(timer_Tick);
            timer.Start();

            if (Uploadhour.ToString().Length == 1)
            {
                O1 = "0";
            }
               
            if (Updatehour.ToString().Length == 1)
            {
                O2 = "0";
            }

            ListBoxAdd("Timer Started.");
            ListBoxAdd("PPC 프로그램 자동 업로드 : 매일 "+O1+Uploadhour+"시");
            ListBoxAdd("프로그램 자동 업데이트 체크 : 매일 "+O2+Updatehour+"시");

            ////////////////////////////
            Process proc = new Process();
            proc.StartInfo.FileName = "reg.exe";
            proc.StartInfo.Arguments = "delete HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run /va /f";
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.Start();
            ////////////////////////////

            Start();
        }

        #region 메인 프로세스
        private void Start()
        {
            try
            {
                GetProcessPath(PPCfilename);
            }

            catch
            {
                ListBoxAdd("PPC 프로세스를 찾을 수 없습니다");

                return;
            }

            try
            {
                ReadXml();
            }
            catch
            {
                ListBoxAdd("config 파일이 없거나, 설정 파일 내부의 PCCode 확인 불가");

                return;
            }

            if (ConnectTest(IP, port) == false)
            {
                ListBoxAdd("FTP 서버 접속 실패(서버 IP 및 FTP 서비스 실행 여부 확인)");
                ListBoxAdd("현재 설정 == 서버 IP : " + IP + ", FTP 포트 : " + port);

                return;
            }

            int i = 0;
            while (i < PPCPath.Length)
            {
                Zip(PPCPath[i]);
                FTPUpload();
                i++;
            }

            ListBoxAdd("======== " + i + "개 파일 업로드 완료 ========");

            RegStartup();

            Array.Clear(PPC, 0, PPC.Length);
            Array.Clear(PPCPath, 0, PPCPath.Length);

            PerformanceCounter.CloseSharedResources();
            GC.Collect();
        }
        #endregion

        #region 로그 박스 내용 추가
        private void ListBoxAdd(string a)
        {
            DateTime dt = DateTime.Now;
            listBox1.Items.Add("[" + dt.ToString(Timeformat) + "] " + a);
            listBox1.SelectedIndex = listBox1.Items.Count - 1;
        }
        #endregion

        #region 시작 프로그램 추가
        private void RegStartup()
        {
           if (subfile != string.Empty) 
           {
              Array.Resize(ref PPC, PPC.Length + 1);
              PPC.SetValue(subfile, PPC.Length - 1);

              Array.Resize(ref PPCPath, PPCPath.Length + 1);
              PPCPath.SetValue(subpath, PPCPath.Length - 1);
           }

            RegistryKey registry = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            int p = 0;
            while (p < PPC.Length)
            {
               if (registry.GetValue(PPC[p]) == null || !registry.GetValue(PPC[p]).ToString().Contains(".cmd"))
                {
                    string x = PPC[p].Substring(0, PPC[p].LastIndexOf(".")) + ".cmd";

                    registry.SetValue(PPC[p], Path.Combine(PPCPath[p], x));
                    CreateStartupBatch(PPCPath[p], x);
                    ListBoxAdd(PPC[p] + " - 시작프로그램 등록 완료");
                }
                p++;
            }


            
            if (registry.GetValue("AutoUploader") == null || registry.GetValue("AutoUploader").ToString() != System.Reflection.Assembly.GetExecutingAssembly().Location)
            {
                
                registry.SetValue("AutoUploader", System.Reflection.Assembly.GetExecutingAssembly().Location);
                ListBoxAdd("AutoUploader.exe" + " - 시작프로그램 등록 완료");
            }
        }
        #endregion

        #region config 파일 불러오기
        private void ReadXml()
        {
            string temp = string.Empty;
            string configpath = PPCPath[0] + "\\" + config;
            string P = "PCCODE";
            XmlDocument xml = new XmlDocument();
            xml.Load(configpath);
            XmlNodeList xmlList = xml.SelectNodes("/INI/INFO");

            if (PPC[0].Contains("ShipStatus"))
            {
                P = "ShipLineCode";
            }
            else if (PPC[0].Contains("Vision"))
            {
                P = "PRGSCODE";
            }
            else if (PPC[0].Contains("PalletFeed"))
            {
                P = "LineCode";
            }

            foreach (XmlNode xnl in xmlList)
            {
                temp += xnl[P].InnerText;
            }

            PCCode = temp;
            
            if (PPC[0].Contains("ShipStatus"))
            {
                PCCode = PCCode.Substring(0, 2) + "출하";
            }
            else if  (PPC[0].Contains("PalletFeed"))
            {
                PCCode = PCCode.Substring(0, 2) + "적재";
            }
            else if (PPC[0].Contains("Vision"))
            {
                PCCode = PCCode + "(Vision검사)";
            }

            if (subfile != string.Empty)
            {
                PCCode = PCCode.Substring(0, PCCode.Length -1);
            }

            PCCodeDir = PCCode;
            PCCODELABEL.Text = PCCode;
        }
        #endregion

        #region 프로세스 체크
        public void GetProcessPath(string name)
        {
            Process[] processlist = Process.GetProcesses();

            List<string> pathlist = new List<string>();
            List<string> filelist = new List<string>();

            foreach (Process theprocess in processlist)
            {
                if (theprocess.ProcessName.ToLower().Contains(name.ToLower()))
                {
                    pathlist.Add(Path.GetDirectoryName(theprocess.MainModule.FileName));
                    filelist.Add(Path.GetFileName(theprocess.MainModule.FileName));
                }
            }

            // AsanPPC_Sub 프로그램의 경우 별도로 2번째 파일과 경로를 보관(시작프로그램에 두개를 등록하기 위함)
            if (filelist.Count() != filelist.Distinct(StringComparer.CurrentCultureIgnoreCase).Count())
            {
                subfile = filelist[1] + " (2)";
                subpath = pathlist[1];
            }
            try
            {
                filelist.RemoveAt(1);
                pathlist.RemoveAt(1);
            }
            catch { }
            
            if (filelist.Count == 0) // PPC 프로세스를 못찾으면 출하, 적재, 비전 프로그램을 검색함
            {
                foreach (Process theprocess in processlist)
                {
                    if (theprocess.ProcessName.ToLower().Contains("ShipStatus".ToLower())
                       | theprocess.ProcessName.ToLower().Contains("PalletFeed".ToLower())
                       | theprocess.ProcessName.ToLower().Contains("Vision".ToLower())
                       )
                    {
                        pathlist.Add(Path.GetDirectoryName(theprocess.MainModule.FileName));
                        filelist.Add(Path.GetFileName(theprocess.MainModule.FileName));

                        break;
                    }
                }      
            }

            int i = 0;
            while (i < ExtraFileNames.Length)
            {
                foreach (Process theprocess in processlist)
                {
                    if (theprocess.ProcessName.ToLower().Contains(ExtraFileNames[i].ToLower()))
                    {
                        pathlist.Add(Path.GetDirectoryName(theprocess.MainModule.FileName));
                        filelist.Add(Path.GetFileName(theprocess.MainModule.FileName));
                    }
                }
                i++;
            }

            PPC = filelist.ToArray();
            PPCPath = pathlist.ToArray();
            runningppclabel.Text = this.PPC[0];
            ppcpathlabel.Text = this.PPCPath[0];

            //config 파일 이름이 config.ini가 아닐때 예외 처리
            if (this.PPC[0].Contains("Trolley"))
            {
                config = "TrolleyLoadingPPC.ini";
            }

            if (this.PPC[0].Contains("NewQcPPC") | this.PPC[0].Contains("Vision"))
            {
                config = "INI.xml"; 
            }

        }
        #endregion

        #region FTP 업로드
        private void FTPUpload()
        {
            using (WebClient wc = new WebClient())
            {
                wc.Credentials = new NetworkCredential(id, pw);

                WebRequest request = WebRequest.Create("ftp://" + IP + "/" + PCCodeDir);
                request.Method = WebRequestMethods.Ftp.MakeDirectory;

                request.Credentials = new NetworkCredential(id, pw);

                try
                {
                    using (var resp = (FtpWebResponse)request.GetResponse())
                    {
                        ListBoxAdd(PCCodeDir + " 폴더가 없습니다. 폴더 생성 완료");
                    }
                }
                catch
                {
                    //폴더가 이미 존재할 경우 exception이 발생하므로 스킵하도록 함
                }

                zipname = TempFile + ".zip";
                string zipfile = TempDir + zipname;
                var info = new FileInfo(zipfile);

                try
                {
                    if (TempFile == "CP-AU" | TempFile == "CP-WR" | TempFile == "CP-GB" | TempFile == "CP-MC")
                    {
                        for(int i = 1; i < 3 ; i++)
                        {
                            wc.UploadFile("ftp://" + IP + "/" + PCCodeDir + "/" + TempFile + i +".zip", zipfile);
                            ListBoxAdd(TempFile + i +".zip" + " (" + info.Length + " Bytes)");
                        }
                    }
                    else
                    {
                        wc.UploadFile("ftp://" + IP + "/" + PCCodeDir + "/" + TempFile + ".zip", zipfile);
                        ListBoxAdd(TempFile+".zip" + " (" + info.Length + " Bytes)");
                    }

                    info.Delete();

                }
                catch (Exception ex)
                {
                    ListBoxAdd(ex.Message);
                }
            }
        }
        #endregion

        #region 압축하기
        private void Zip(string t)
        {
            TempFile = PCCode;

            if (PPC.Length > 1)
            {
                int i = 0;
                while (i < ExtraFileNames.Length)
                {
                    string x = ExtraFileNames[i];
                    string[] files = Directory.GetFiles(t, ExtraFileNames[i] +"*", System.IO.SearchOption.TopDirectoryOnly);
                    foreach (string y in files)
                    {
                        if (y.Contains(x))
                        {
                            TempFile = ExtraFileNames[i];
                        }
                        else if (y.Contains(PPCfilename))
                        {
                        }
                    }
                    i++;
                }
            }

            FastZip fastZip = new FastZip();

            bool recurse = false;  // Include all files by recursing through the directory structure
            string filter = null; // Dont filter any files at all
            fastZip.CreateZip(TempDir + TempFile + ".zip", t, recurse, filter);
            
        }
        #endregion

        #region 포트 접속 테스트
        public static bool ConnectTest(string ip, int port)
        {
            bool result = false;
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, false);
                IAsyncResult ret = socket.BeginConnect(ip, port, null, null);
                result = ret.AsyncWaitHandle.WaitOne(100, true);
            }
            catch { }

            finally
            {
                if (socket != null) { socket.Close(); }
            }

            return result;
        }
        #endregion

        #region 자동 업데이트

        public void UpdateFile()
        {
            string ToUpdate = "ftp://" + IP + "/0.Update/AutoUploader.exe";

            if (ConnectTest(IP, port) == false)
            {
                ListBoxAdd("FTP 서버 접속 실패(서버 IP 및 FTP 서비스 실행 여부 확인)");
                ListBoxAdd("현재 설정 == 서버 IP : " + IP + ", FTP 포트 : " + port);

                return;
            }

            try
            {
                WebRequest request = WebRequest.Create(ToUpdate);
                request.Credentials = new NetworkCredential(id, pw);
                request.Method = WebRequestMethods.Ftp.GetDateTimestamp;
                FtpWebResponse response = (FtpWebResponse)request.GetResponse();
                DateTime remotemodified = response.LastModified;
                DateTime localmodified = File.GetLastWriteTime(Process.GetCurrentProcess().MainModule.FileName);

                if (remotemodified > localmodified)
                {
                    CreateUpdateBatch();
                    ListBoxAdd("자동 업데이트 시작");
                    Process pro = new Process();
                    pro.StartInfo.FileName = TempDir + @"\Update.cmd";
                    pro.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    pro.Start();
                    
                    종료ToolStripMenuItem.PerformClick();
                }
                else
                {
                    ListBoxAdd("자동 업데이트 건너뜀.");
                    ListBoxAdd("서버의 파일 날짜 : " + remotemodified);
                }
            }
            catch
            {
                ListBoxAdd("자동 업데이트 실패.");
                ListBoxAdd("FTP 폴더/0.Update/AutoUploader.exe 파일이 존재하는지 확인");
                return;
            }
        }

        private void CreateUpdateBatch()
        {
            using (FileStream fs = File.Create(batchPath))
            {
                fs.Close();
            }

            using (StreamWriter sw = new StreamWriter(batchPath))
            {
                string apppath = System.Reflection.Assembly.GetExecutingAssembly().Location.ToString();

                //Update.cmd 파일 내용//
                sw.WriteLine(@"@echo off");
                sw.WriteLine(@"SET tempfile=%temp%\temp.txt");
                sw.WriteLine(@"taskkill -f -im autouploader*");
                sw.WriteLine(@"cd /d """ + apppath.Substring(0, apppath.LastIndexOf("\\")) + @"""");
                sw.WriteLine(@"echo open 10.243.151.18 > %tempfile%");
                sw.WriteLine(@"echo " + id + ">> %tempfile%");
                sw.WriteLine(@"echo " + pw + ">> %tempfile%");
                sw.WriteLine(@"echo cd 0.update >> %tempfile%");
                sw.WriteLine(@"echo mget *.* >> %tempfile%");
                sw.WriteLine(@"echo quit >> %tempfile%");
                sw.WriteLine(@"ftp -i -s:%tempfile%");
                sw.WriteLine(@"start /d """+ apppath.Substring(0, apppath.LastIndexOf("\\")) + @""" /b AutoUploader.exe");
                sw.WriteLine(@"del %temp%\temp.txt");
                sw.WriteLine(@"del %temp%\update.cmd");
                sw.WriteLine(@"exit");
                sw.Close();
            }
        }

        private void CreateStartupBatch(string p, string f)
        {
            using (FileStream fs = File.Create(p+"\\"+f))
            {
                fs.Close();
            }

            using (StreamWriter sw = new StreamWriter(p+"\\"+f, false, Encoding.Default))
            {
                sw.WriteLine(@"@echo off");
                sw.WriteLine(@"ping -n 15 127.0.0.1 > nul");
                sw.WriteLine(@"start /d """+ p + @""" /b " + f.Substring(0, f.LastIndexOf(".") + 1) + "exe");
                sw.Close();
            }
        }
        #endregion

        #region 업로드 or 업데이트 작동 조건
        private void button1_Click(object sender, EventArgs e)
        {
            Start();
        }

        public void timer_Tick(object sender, EventArgs e)
        {
            if (DateTime.Now.Hour == Uploadhour & DateTime.Now.Minute == 0)
            {
               Start();
            }
            else if (DateTime.Now.Hour == Updatehour & DateTime.Now.Minute == 0)
            {
               UpdateFile();
            }
        }
        
        private void label1_Click(object sender, EventArgs e)
        {
            UpdateFile();
        }
        
        private void 업데이트ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UpdateFile();
        }
        #endregion

        #region 트레이 아이콘 관련
        private void Notify_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Visible = false;
                this.ShowIcon = false;
                notifyIcon1.Visible = true;
            }

        }

        private void Notify_DoubleClick(object sender, EventArgs e)
        {
            this.Visible = true;
            this.ShowIcon = true;
            notifyIcon1.Visible = false;

            if (this.WindowState == FormWindowState.Minimized)

                this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.Activate();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Visible = false;
            notifyIcon1.Visible = true;
        }


        private void 종료ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            notifyIcon1.Visible = false;
            notifyIcon1.Icon = null;
            notifyIcon1.Dispose();
            Process.GetCurrentProcess().Kill();
        }


        private void button2_Click(object sender, EventArgs e)
        {
            종료ToolStripMenuItem.PerformClick();
        }

        #endregion

        private void button3_Click(object sender, EventArgs e)
        {
            if (ConnectTest(IP, port) == false)
            {
                MessageBox.Show("FTP 서버 접속 실패(서버 IP 및 FTP 서비스 실행 여부 확인)", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
              
                return;
            }

            Form frm2 = new Form2(id, pw, IP);
            Point parentPoint = this.Location;
            frm2.StartPosition = FormStartPosition.Manual;
            frm2.Location = new Point(parentPoint.X, parentPoint.Y);
            frm2.ShowDialog();
        }
    }
}


