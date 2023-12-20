/*
 * Copyright (c) Contributors, http://opensimulator.org/, http://www.nsl.tuis.ac.jp/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *	 * Redistributions of source code must retain the above copyright
 *	   notice, this list of conditions and the following disclaimer.
 *	 * Redistributions in binary form must reproduce the above copyright
 *	   notice, this list of conditions and the following disclaimer in the
 *	   documentation and/or other materials provided with the distribution.
 *	 * Neither the name of the OpenSim Project nor the
 *	   names of its contributors may be used to endorse or promote products
 *	   derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Timers;
//using System.Security.Authentication;
//using System.Security.Cryptography;
//using System.Security.Cryptography.X509Certificates;

using HttpServer;
using Nini.Config;
using log4net;

using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Data;

using NSL.Certificate.Tools;



namespace OpenSim.Grid.MoneyServer
{
	class MoneyServerBase : BaseOpenSimServer, IMoneyServiceCore
	{
		private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		private string connectionString = string.Empty;
		private uint m_moneyServerPort = 8008;

		private string m_certFilename	 = "";
		private string m_certPassword	 = "";
		private string m_cacertFilename  = "";
		private string m_clcrlFilename	 = "";
		private bool   m_checkClientCert = false;

		private int DEAD_TIME            = 120;
		private int MAX_DB_CONNECTION    = 10;

		private MoneyXmlRpcModule m_moneyXmlRpcModule;
		private MoneyDBService m_moneyDBService;

		private NSLCertificateVerify m_certVerify = new NSLCertificateVerify();	// クライアント認証用

		private Dictionary<string, string> m_sessionDic = new Dictionary<string, string>();
		private Dictionary<string, string> m_secureSessionDic = new Dictionary<string, string>();
		private Dictionary<string, string> m_webSessionDic = new Dictionary<string, string>();

		IConfig m_server_config;
		IConfig m_cert_config;


		public MoneyServerBase()
		{
			m_console = new LocalConsole("Money ");
//			m_console = new CommandConsole("Money ");
			MainConsole.Instance = m_console;
		}


		public void Work()
		{
			//m_console.Notice("Enter help for a list of commands\n");

			//The timer checks the transactions table every 60 seconds
			Timer checkTimer = new Timer();
			checkTimer.Interval = 60*1000;
			checkTimer.Enabled = true;
			checkTimer.Elapsed += new ElapsedEventHandler(CheckTransaction);
			checkTimer.Start();

			while (true) {
				m_console.Prompt();
			}
		}


		/// <summary>
		/// Check the transactions table, set expired transaction state to failed
		/// </summary>
		private void CheckTransaction(object sender, ElapsedEventArgs e)
		{
			long ticksToEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
			int unixEpochTime =(int) ((DateTime.UtcNow.Ticks - ticksToEpoch )/10000000);
			int deadTime = unixEpochTime - DEAD_TIME;
			m_moneyDBService.SetTransExpired(deadTime);
		}


		protected override void StartupSpecific()
		{
			m_log.Info("[MONEY SERVER]: Setup HTTP Server process");

			ReadIniConfig();

			try {
				if (m_certFilename!="") {
					m_httpServer = new BaseHttpServer(m_moneyServerPort, true, m_certFilename, m_certPassword);
					if (m_checkClientCert) {
						Type typeBaseHttpServer = typeof(BaseHttpServer); // BaseHttpServer.cs にパッチがあたっていない場合のため
						PropertyInfo pinfo = typeBaseHttpServer.GetProperty("CertificateValidationCallback");	

						if (pinfo!=null) {
							//m_httpServer.CertificateValidationCallback = (RemoteCertificateValidationCallback)m_certVerify.ValidateClientCertificate; 
 							pinfo.SetValue(m_httpServer, (RemoteCertificateValidationCallback)m_certVerify.ValidateClientCertificate, null);
							m_log.Info ("[MONEY SERVER]: Set RemoteCertificateValidationCallback");
						}
						else {
							m_log.Error("[MONEY SERVER]: StartupSpecific: CheckClientCert is true. But this MoneyServer does not support CheckClientCert!!");
						}
					}
				}
				else {
					m_httpServer = new BaseHttpServer(m_moneyServerPort);
				}

				SetupMoneyServices();
				m_httpServer.Start();
				base.StartupSpecific();		// OpenSim/Framework/Servers/BaseOpenSimServer.cs 
			}

			catch (Exception e) {
				m_log.ErrorFormat("[MONEY SERVER]: StartupSpecific: Fail to start HTTPS process");
				m_log.ErrorFormat("[MONEY SERVER]: StartupSpecific: Please Check Certificate File or Password. Exit");
				m_log.ErrorFormat("[MONEY SERVER]: StartupSpecific: {0}", e);
				Environment.Exit(1);
			}

			//TODO : Add some console commands here
		}


		protected void ReadIniConfig()
		{
			MoneyServerConfigSource moneyConfig = new MoneyServerConfigSource();
			Config = moneyConfig.m_config;	// for base.StartupSpecific()

			try {
				// [Startup]
				IConfig st_config = moneyConfig.m_config.Configs["Startup"];
				string PIDFile = st_config.GetString("PIDFile", "");
				if (PIDFile!="") Create_PIDFile(PIDFile);

				// [MySql]
				IConfig db_config = moneyConfig.m_config.Configs["MySql"];
				string sqlserver  = db_config.GetString("hostname", "localhost");
				string database   = db_config.GetString("database", "OpenSim");
				string username   = db_config.GetString("username", "root");
				string password   = db_config.GetString("password", "password");
				string pooling 	  = db_config.GetString("pooling",  "false");
				string port 	  = db_config.GetString("port", 	"3306");
				MAX_DB_CONNECTION = db_config.GetInt   ("MaxConnection", MAX_DB_CONNECTION);

				connectionString  = "Server=" + sqlserver + ";Port=" + port + ";Database=" + database + ";User ID=" +
												username + ";Password=" + password + ";Pooling=" + pooling + ";";

				// [MoneyServer]
				m_server_config = moneyConfig.m_config.Configs["MoneyServer"];
				DEAD_TIME = m_server_config.GetInt("ExpiredTime", DEAD_TIME);

				//
				// [Certificate]
				m_cert_config = moneyConfig.m_config.Configs["Certificate"];
				if (m_cert_config==null) {
					m_log.Info("[MONEY SERVER]: [Certificate] section is not found. Using [MoneyServer] section instead");
					m_cert_config = m_server_config;
				}

				// HTTPS Server Cert (Server Mode)
				// サーバ証明書
				m_certFilename = m_cert_config.GetString("ServerCertFilename", m_certFilename);
				m_certPassword = m_cert_config.GetString("ServerCertPassword", m_certPassword);
				if (m_certFilename!="") {
					m_log.Info("[MONEY SERVER]: ReadIniConfig: Execute HTTPS comunication. Cert file is " + m_certFilename);
				}

				// クライアント認証
				m_checkClientCert = m_cert_config.GetBoolean("CheckClientCert",  m_checkClientCert);
				m_cacertFilename  = m_cert_config.GetString("CACertFilename",    m_cacertFilename);
				m_clcrlFilename   = m_cert_config.GetString("ClientCrlFilename", m_clcrlFilename);
				//
				if (m_checkClientCert && m_cacertFilename!="") {
					m_certVerify.SetPrivateCA(m_cacertFilename);
					m_log.Info("[MONEY SERVER]: ReadIniConfig: Execute Authentication of Clients. CA  file is " + m_cacertFilename);
				}
				else {
					m_checkClientCert = false;
				}

				if (m_checkClientCert) {
					if (m_clcrlFilename!="") {
						m_certVerify.SetPrivateCRL(m_clcrlFilename);
						m_log.Info("[MONEY SERVER]: ReadIniConfig: Execute Authentication of Clients. CRL file is " + m_clcrlFilename);
					}
				}
			}

			catch (Exception) {
				m_log.Error("[MONEY SERVER]: ReadIniConfig: Fail to setup configure. Please check MoneyServer.ini. Exit");
				Environment.Exit(1);
			}
		}


		// added by skidz
		protected void Create_PIDFile(string path)
		{
			try {
				string pidstring = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
				FileStream fs = File.Create(path);
				System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();
				Byte[] buf = enc.GetBytes(pidstring);
				fs.Write(buf, 0, buf.Length);
				fs.Close();
				m_pidFile = path;
			}
			catch (Exception) {
			}
		}


		protected virtual void SetupMoneyServices()
		{
			m_log.Info("[MONEY SERVER]: Connecting to Money Storage Server");

			m_moneyDBService = new MoneyDBService();
			m_moneyDBService.Initialise(connectionString, MAX_DB_CONNECTION);

			m_moneyXmlRpcModule = new MoneyXmlRpcModule();
//			m_moneyXmlRpcModule.Initialise(m_version, m_server_config, m_cert_config, m_moneyDBService, this);
			m_moneyXmlRpcModule.Initialise(m_version, m_moneyDBService, this);
			m_moneyXmlRpcModule.PostInitialise();
		}


		//
		public bool IsCheckClientCert()
		{
			return m_checkClientCert;
		}


		public IConfig GetServerConfig()
		{
			return m_server_config;
		}


		public IConfig GetCertConfig()
		{
			return m_cert_config;
		}


		public BaseHttpServer GetHttpServer()
		{
			return m_httpServer;
		}


		public Dictionary<string, string> GetSessionDic()
		{
			return m_sessionDic;
		}


		public Dictionary<string, string> GetSecureSessionDic()
		{
			return m_secureSessionDic;
		}


		public Dictionary<string, string> GetWebSessionDic()
		{
			return m_webSessionDic;
		}

	}



	//
	class MoneyServerConfigSource
	{
		public IniConfigSource m_config;

		public MoneyServerConfigSource()
		{
			string configPath = Path.Combine(Directory.GetCurrentDirectory(), "MoneyServer.ini");
			if (File.Exists(configPath)) {
				m_config = new IniConfigSource(configPath);
			}
			else {
				//TODO: create default configuration.
				//m_config = DefaultConfig();
			}
		}


		public void Save(string path)
		{
			m_config.Save(path);
		}

	}
}
