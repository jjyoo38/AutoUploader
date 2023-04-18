using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Net;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ICSharpCode.SharpZipLib.Zip;
using System.IO;
using System.Diagnostics;
using System.Xml;

namespace AutoUploader
{
    public partial class Form2 : Form
    {
        string id1 = string.Empty;
        string pw1 = string.Empty;
        string ip1 = string.Empty;
        string addr = string.Empty;
        string TargetDrive = "C";
        string TargetFile = string.Empty;
        string TargetPath = string.Empty;
        string PCCode = string.Empty;

        public Form2(string id, string pw, string ip)
        {
            //Form1에서 FTP 접속 정보 변수 파라미터를 가져옴
            InitializeComponent();
            id1 = id;
            pw1 = pw;
            ip1 = ip;
        }

        private void Form2_Load(object sender, EventArgs e)
        {
            // 복원하기 버튼을 눌러서 Form2가 로드되면 datagridview에 FTP서버에 있는 PCCode 이름으로 된 모든 폴더를 불러와서 바인딩함.
            List<string> directories = new List<string>();
            List<string> filedate = new List<string>();

            SetFTPtoList("ftp://" + ip1, directories);

            string[] dirArray = directories.ToArray();
            
            foreach (string dir in dirArray)
            {
                if (dir == "CP-AU" | dir == "CP-WR" | dir == "CP-MC" | dir == "CP-GB")
                {
                    addr = string.Format("ftp://{0}/{1}/{2}.zip", ip1, dir, dir+"1");
                }
                else
                {
                    addr = string.Format("ftp://{0}/{1}/{1}.zip", ip1, dir);
                }
                

                try
                {
                    WebRequest request = WebRequest.Create(addr);
                    request.Credentials = new NetworkCredential(id1, pw1);
                    request.Method = WebRequestMethods.Ftp.GetDateTimestamp;
                    FtpWebResponse response1 = (FtpWebResponse)request.GetResponse();
                                        
                    filedate.Add(response1.LastModified.ToString());
                }
                catch
                {
                    filedate.Add("파일 없음");
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("PCCode");
            dt.Columns.Add("백업 날짜");

            for (int i = 0; i < directories.Count; i++)
            {
                dt.Rows.Add(directories[i], filedate[i]);
            }

            dataGridView1.DataSource = dt;
            dataGridView1.ReadOnly = true;
            dataGridView1.EnableHeadersVisualStyles = false;
            dataGridView1.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(230,230,230);
            dataGridView1.RowHeadersVisible = false;
            dataGridView1.CellBorderStyle = DataGridViewCellBorderStyle.None;
            dataGridView1.ClearSelection();
        }

        private void SetFTPtoList(string sourcepath, List<string> ToInsertList)
        {
            // 해당 FTP 경로의 폴더, 파일을 모두 수집하여 List에 넣어주는 메소드임
            FtpWebRequest ftpRequest = (FtpWebRequest)WebRequest.Create(sourcepath);
            ftpRequest.Credentials = new NetworkCredential(id1, pw1);
            ftpRequest.Method = WebRequestMethods.Ftp.ListDirectory;
            FtpWebResponse response = (FtpWebResponse)ftpRequest.GetResponse();
            StreamReader streamReader = new StreamReader(response.GetResponseStream());

            string line = streamReader.ReadLine();

            while (!string.IsNullOrEmpty(line))
            {

                if (line.Substring(0, 1) != "0")
                //업데이트 폴더는 0으로 시작하므로 제외시키기 위해 if절 추가
                {
                    ToInsertList.Add(line);
                }

                line = streamReader.ReadLine();
            }
            streamReader.Close();
        }

        private void DownloadButton_Click(object sender, EventArgs e)
        {
            if (PCCode == string.Empty)
            {
                MessageBox.Show("PCCode가 선택되지 않았습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
    
            // D드라이브가 존재하면 D에 설치, 없으면 C에 설치. 단, D가 로컬디스크(Fixed) 일때만
            DriveInfo[] driveInfoArray = DriveInfo.GetDrives();

            foreach (DriveInfo driveInfo in driveInfoArray)
            {
                if (driveInfo.Name.ToString() == "D:\\" & driveInfo.DriveType.ToString() == "Fixed")
                {
                    TargetDrive = "D";
                }
            }

            List<string> sourcefiles = new List<string>();

            // 선택한 PCCode 폴더에 있는 파일들을 sourcefiles List에 넣어준다
            SetFTPtoList("ftp://" + ip1 + "/" + PCCode, sourcefiles);
            
            string[] filesArray = sourcefiles.ToArray();

            // 다운받을 파일 목록을 filesArray 배열에 넣고 foreach 문을 이용하여 다운->압축풀기 반복함
            int i = 0;
            List<string> installpath = new List<string>();

            foreach (string downfile in filesArray)
            {
                addr = string.Format("ftp://{0}/{1}/{2}", ip1, PCCode, downfile);
                TargetFile = string.Format("{0}:\\Workspace\\{1}", TargetDrive, downfile);
                TargetPath = string.Format("{0}:\\Workspace\\{1}", TargetDrive, downfile.Substring(0, downfile.LastIndexOf(".")));

                if (Directory.Exists(TargetPath) == false)
                {  
                   Directory.CreateDirectory(TargetPath);
                }

                else

                {
                    string[] getdirectoryfiles = Directory.GetFiles(TargetPath);

                    if (getdirectoryfiles.Length > 0)
                    {
                        if (DialogResult.Cancel == MessageBox.Show("대상 폴더(" + TargetPath + ")에 파일이 존재합니다.\r\r덮어쓰시겠습니까?", "경고", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning))
                        {
                            return;
                        }
                    }
                }
                
                
                try

                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.Credentials = new NetworkCredential(id1, pw1);
                        wc.DownloadFile(addr, TargetFile);
                        
                        FastZip fastzip = new FastZip();
                        fastzip.ExtractZip(TargetFile, TargetPath, FastZip.Overwrite.Always, null, null, null, true);
                        i++;
                        installpath.Add(TargetPath);

                        if (downfile.Contains("CP-AU") | downfile.Contains("CP-MC") | downfile.Contains("CP-WR") | downfile.Contains("CP-GB"))
                        {
                            //AsanPPC_Sub 프로그램은 2개로 나눠져 있으므로 설치와 동시에 각각의 config.ini에 있는 PCCode를 수정해줘야함(ex. CP-AU1,CP-AU2)
                            XmlDocument xml = new XmlDocument();
                            xml.Load(TargetPath + "\\config.ini");
                            XmlNode root = xml.DocumentElement;
                            XmlNode node = root.SelectSingleNode("/INI/INFO/PCCODE");
                            node.InnerText = downfile.Substring(0, downfile.LastIndexOf(".")); // 현재 압축파일명을 Config.ini 내부의 PCCode에 넣어준다

                            xml.Save(TargetPath + "\\config.ini");
                        }
                    }

                    FileInfo fileinfo = new FileInfo(TargetFile);
                    fileinfo.Delete();
                    
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
            string[] installpatharray = installpath.ToArray();
            string toDisplay = string.Join(Environment.NewLine, installpatharray);
            MessageBox.Show(i + "개의 프로그램이 다운로드 완료되었습니다.\r\r설치된 경로 : \r"+toDisplay, "정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            PCCode = dataGridView1.Rows[dataGridView1.CurrentRow.Index].Cells[0].Value.ToString();

            if(dataGridView1.Rows[dataGridView1.CurrentRow.Index].Cells[1].Value.ToString() == "파일 없음")
            {
                DownloadButton.Enabled = false;
            }
            else 
            {
                DownloadButton.Enabled = true;
            }

        }

        private void BackButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            GC.Collect();
        }
    }
}
