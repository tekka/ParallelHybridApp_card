using Newtonsoft.Json;
using SuperWebSocket;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Net.Sockets;
using System.Net;
using System.Configuration;
using System.Text;
using NfcPcSc;

namespace ParallelHybridApp
{

    public partial class AppServer : Form
    {
        public List<String> log_ary = new List<string>();
        public static AppServer frm;
        public Dictionary<string, WebSocketSession> session_ary = new Dictionary<string, WebSocketSession>();

        SuperWebSocket.WebSocketServer server_ssl;


        bool _is_connect = false;
        IntPtr _hContext;
        NfcApi.SCARD_READERSTATE[] _readerStateArray;
        string _readername;
        uint _prev_state_is_present;
        uint _prev_state_is_empty;


        public AppServer()
        {
            InitializeComponent();
        }


        private void Form1_Load(object sender, EventArgs e)
        {
            frm = this;

            try
            {
                var server_config_ssl = new SuperSocket.SocketBase.Config.ServerConfig()
                {
                    Port = 443,
                    Ip = "127.0.0.1",
                    MaxConnectionNumber = 100,
                    Mode = SuperSocket.SocketBase.SocketMode.Tcp,
                    Name = "SuperWebSocket Sample Server",
                    MaxRequestLength = 1024 * 1024 * 10,
                    Security = "tls",
                    Certificate = new SuperSocket.SocketBase.Config.CertificateConfig
                    {
                        FilePath = ConfigurationManager.AppSettings["cert_file_path"],
                        Password = ConfigurationManager.AppSettings["cert_password"]
                    }
                };

                setup_server(ref server_ssl, server_config_ssl);

                valid_cert();

                var result = NfcApi.SCardEstablishContext(
                    NfcConstant.SCARD_SCOPE_USER,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    out _hContext
                    );

                if (result != 0)
                {
                    if (result == NfcConstant.SCARD_E_NO_SERVICE)
                    {
                        frm.add_log(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), "Smart Card Servise is not Started.");
                    }
                    else
                    {
                        frm.add_log(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), string.Format("SCardEstablishContext Error.\nErrorCode.%08X\n", result));
                    }
                }
                else
                {
                    _is_connect = true;

                    try
                    {
                        uint pcchReaders = 0;

                        result = NfcApi.SCardListReaders(_hContext, null, null, ref pcchReaders);

                        if (result != NfcConstant.SCARD_S_SUCCESS)
                        {
                            throw new ApplicationException("リーダーの情報が取得できません。");
                        }

                        byte[] mszReaders = new byte[pcchReaders * 2];

                        result = NfcApi.SCardListReaders(_hContext, null, mszReaders, ref pcchReaders);

                        if (result != NfcConstant.SCARD_S_SUCCESS)
                        {
                            throw new ApplicationException("リーダーの情報が取得できません。");
                        }

                        UnicodeEncoding unicodeEncoding = new UnicodeEncoding();
                        string readerNameMultiString = unicodeEncoding.GetString(mszReaders);

                        int nullindex = readerNameMultiString.IndexOf((char)0);   // 装置は１台のみ
                        _readername = readerNameMultiString.Substring(0, nullindex);

                        frm.add_log(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), "リーダー名：" + _readername);

                        _readerStateArray = new NfcApi.SCARD_READERSTATE[1];


                        timer1.Start();
                    }
                    catch (ApplicationException ex)
                    {
                        frm.add_log(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), ex.Message);
                    }
                }

            }
            catch (Exception ex)
            {
                reflesh_cert();

                MessageBox.Show("証明書を更新しました。\nアプリケーションを再起動します。");

                Application.Restart();
            }

        }

        private void setup_server(ref WebSocketServer server, SuperSocket.SocketBase.Config.ServerConfig serverConfig)
        {
            var rootConfig = new SuperSocket.SocketBase.Config.RootConfig();

            server = new SuperWebSocket.WebSocketServer();

            //サーバーオブジェクト作成＆初期化
            server.Setup(rootConfig, serverConfig);

            //イベントハンドラの設定
            //接続
            server.NewSessionConnected += HandleServerNewSessionConnected;
            //メッセージ受信
            server.NewMessageReceived += HandleServerNewMessageReceived;
            //切断        
            server.SessionClosed += HandleServerSessionClosed;

            //サーバー起動
            server.Start();

        }


        //接続
        static void HandleServerNewSessionConnected(SuperWebSocket.WebSocketSession session)
        {
            frm.session_ary.Add(session.SessionID, session);

            frm.Invoke((MethodInvoker)delegate ()
            {
                frm.add_log(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), "接続");
            });

        }

        //メッセージ受信
        static void HandleServerNewMessageReceived(SuperWebSocket.WebSocketSession session,
                                                    string e)
        {
            frm.Invoke((MethodInvoker)delegate ()
            {
                MessageData recv = JsonConvert.DeserializeObject<MessageData>(e);

                switch (recv.command)
                {
                    case "add_message_to_app":

                        frm.add_log(recv.time, "受信: " + recv.message);

                        break;
                }

            });

        }

        //切断
        static void HandleServerSessionClosed(SuperWebSocket.WebSocketSession session,
                                                    SuperSocket.SocketBase.CloseReason e)
        {
            if (frm != null)
            {
                frm.session_ary.Remove(session.SessionID);

                frm.Invoke((MethodInvoker)delegate ()
                {
                    frm.add_log(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), "切断");
                });
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            frm = null;

            if (_is_connect)
            {
                NfcApi.SCardReleaseContext(_hContext);
            }

            server_ssl.Stop();
        }

        public void add_log(string time, String log)
        {
            log = "[" + time + "] " + log + "\r\n";
            this.txtMessage.AppendText(log);
        }

        //メッセージ送信
        private void send_message_to_sessions(string message)
        {
            foreach (var session in session_ary.Values)
            {
                MessageData send = new MessageData();

                send.command = "add_message_to_browser";
                send.message = message;
                send.time = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

                string send_str = JsonConvert.SerializeObject(send);

                session.Send(send_str);

                add_log(send.time, "送信:" + message);
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            send_message_to_sessions(this.txtSendMessage.Text);
        }

        private static Boolean RemoteCertificateValidationCallback(Object sender,
        X509Certificate certificate,
        X509Chain chain,
        SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            return false;
        }

        private void valid_cert()
        {
            String hostName = ConfigurationManager.AppSettings["cert_local_host"];
            Int32 port = 443;

            using (TcpClient client = new TcpClient())
            {
                //接続先Webサーバー名からIPアドレスをルックアップ    
                IPAddress[] ipAddresses = Dns.GetHostAddresses(hostName);

                //Webサーバーに接続する
                client.Connect(new IPEndPoint(ipAddresses[0], port));

                //SSL通信の開始
                using (SslStream sslStream =
                    new SslStream(client.GetStream(), false, RemoteCertificateValidationCallback))
                {
                    //サーバーの認証を行う
                    //これにより、RemoteCertificateValidationCallbackメソッドが呼ばれる
                    sslStream.AuthenticateAsClient(hostName);
                }
            }
        }

        private void reflesh_cert()
        {
            //証明書の更新

            var cert_file_url = ConfigurationManager.AppSettings["cert_file_url"];
            var cert_file_path = ConfigurationManager.AppSettings["cert_file_path"];

            var wc = new WebClient();
            wc.DownloadFile(cert_file_url, cert_file_path);
        }



        IntPtr connect(IntPtr hContext, string readerName)
        {
            IntPtr hCard = IntPtr.Zero;
            IntPtr activeProtocol = IntPtr.Zero;
            uint ret = NfcApi.SCardConnect(hContext, readerName, NfcConstant.SCARD_SHARE_SHARED, NfcConstant.SCARD_PROTOCOL_T1, ref hCard, ref activeProtocol);
            if (ret != NfcConstant.SCARD_S_SUCCESS)
            {
                throw new ApplicationException("カードに接続できません。code = " + ret);
            }
            return hCard;
        }

        void disconnect(IntPtr hCard)
        {
            uint ret = NfcApi.SCardDisconnect(hCard, NfcConstant.SCARD_LEAVE_CARD);
            if (ret != NfcConstant.SCARD_S_SUCCESS)
            {
                throw new ApplicationException("カードとの接続を切断できません。code = " + ret);
            }
        }

        string readReaderSerialNumber(IntPtr hCard)
        {
            int controlCode = 0x003136b0; // SCARD_CTL_CODE(3500) の値 
            // IOCTL_PCSC_CCID_ESCAPE
            // SONY SDK for NFC M579_PC_SC_2.1j.pdf 3.1.1 IOCTRL_PCSC_CCID_ESCAPE
            byte[] sendBuffer = new byte[] { 0xc0, 0x08 }; // ESC_CMD_GET_INFO / Product Serial Number 
            byte[] recvBuffer = new byte[64];
            int recvLength = control(hCard, controlCode, sendBuffer, recvBuffer);

            ASCIIEncoding asciiEncoding = new ASCIIEncoding();
            string serialNumber = asciiEncoding.GetString(recvBuffer, 0, recvLength - 1); // recvBufferには\0で終わる文字列が取得されるので、長さを-1する。
            return serialNumber;
        }
        string readCardId(IntPtr hCard)
        {
            byte maxRecvDataLen = 64;
            byte[] recvBuffer = new byte[maxRecvDataLen + 2];
            byte[] sendBuffer = new byte[] { 0xff, 0xca, 0x00, 0x00, maxRecvDataLen };
            int recvLength = transmit(hCard, sendBuffer, recvBuffer);

            string cardId = BitConverter.ToString(recvBuffer, 0, recvLength - 2).Replace("-", "");
            return cardId;
        }

        int transmit(IntPtr hCard, byte[] sendBuffer, byte[] recvBuffer)
        {
            NfcApi.SCARD_IO_REQUEST ioRecv = new NfcApi.SCARD_IO_REQUEST();
            ioRecv.cbPciLength = 255;

            int pcbRecvLength = recvBuffer.Length;
            int cbSendLength = sendBuffer.Length;
            IntPtr SCARD_PCI_T1 = getPciT1();
            uint ret = NfcApi.SCardTransmit(hCard, SCARD_PCI_T1, sendBuffer, cbSendLength, ioRecv, recvBuffer, ref pcbRecvLength);
            if (ret != NfcConstant.SCARD_S_SUCCESS)
            {
                throw new ApplicationException("カードへの送信に失敗しました。code = " + ret);
            }
            return pcbRecvLength; // 受信したバイト数(recvBufferに受け取ったバイト数)
        }

        private IntPtr getPciT1()
        {
            IntPtr handle = NfcApi.LoadLibrary("Winscard.dll");
            IntPtr pci = NfcApi.GetProcAddress(handle, "g_rgSCardT1Pci");
            NfcApi.FreeLibrary(handle);
            return pci;
        }

        int control(IntPtr hCard, int controlCode, byte[] sendBuffer, byte[] recvBuffer)
        {
            int bytesReturned = 0;
            uint ret = NfcApi.SCardControl(hCard, controlCode, sendBuffer, sendBuffer.Length, recvBuffer, recvBuffer.Length, ref bytesReturned);
            if (ret != NfcConstant.SCARD_S_SUCCESS)
            {
                throw new ApplicationException("カードへの制御命令送信に失敗しました。code = " + ret);
            }
            return bytesReturned;
        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            timer1.Stop();

            try
            {
                _readerStateArray[0].dwCurrentState = NfcConstant.SCARD_STATE_UNAWARE;
                _readerStateArray[0].szReader = _readername;

                uint ret = NfcApi.SCardGetStatusChange(_hContext, 100/*msec*/, _readerStateArray, _readerStateArray.Length);
                if (ret != NfcConstant.SCARD_S_SUCCESS)
                {
                    throw new ApplicationException("リーダーの初期状態の取得に失敗。code = " + ret);
                }

                var state_is_present = _readerStateArray[0].dwEventState & NfcConstant.SCARD_STATE_PRESENT;

                if (_prev_state_is_present != state_is_present &&
                    state_is_present == NfcConstant.SCARD_STATE_PRESENT)
                {
                    IntPtr hCard = connect(_hContext, _readername);
                    string readerSerialNumber = readReaderSerialNumber(hCard);
                    string cardId = readCardId(hCard);


                    var message = "検出:" + _readername + " S/N:" + readerSerialNumber + " ID:" + cardId;

                    send_message_to_sessions(message);

                    frm.add_log(
                        DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                        message
                        );

                    disconnect(hCard);

                }
                var state_is_empty = _readerStateArray[0].dwEventState & NfcConstant.SCARD_STATE_EMPTY;

                if (_prev_state_is_empty != state_is_empty &&
                    state_is_empty == NfcConstant.SCARD_STATE_EMPTY)
                {
                    var message = "未検出:" + _readername;

                    send_message_to_sessions(message);

                    frm.add_log(
                        DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                        message
                        );
                }

                _prev_state_is_present = state_is_present;
                _prev_state_is_empty = state_is_empty;

            }
            catch (ApplicationException ex)
            {
                frm.add_log(
                    DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                    ex.Message
                    );
            }

            timer1.Start();
        }
    }
}
