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
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using log4net;
using Nini.Config;
using Nwc.XmlRpc;

using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Data.MySQL.MySQLMoneyDataWrapper;
using OpenSim.Modules.Currency;

using NSL.Network.XmlRpc;
using NSL.Certificate.Tools;


namespace OpenSim.Grid.MoneyServer
{
	class MoneyXmlRpcModule
	{
		private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		private int	   m_defaultBalance  = 0;
		//
		private bool   m_forceTransfer   = false;
		private string m_bankerAvatar	 = "";

		private bool   m_scriptSendMoney = false;
		private string m_scriptAccessKey = "";
		private string m_scriptIPaddress = "127.0.0.1";

		private bool   m_checkServerCert = false;
		private string m_cacertFilename  = "";

		private string m_certFilename	 = "";
		private string m_certPassword	 = "";
		private X509Certificate2 m_cert  = null;

		private string m_sslCommonName   = "";

		private NSLCertificateVerify m_certVerify = new NSLCertificateVerify();		// サーバ認証用


		// Update Balance Messages
		private string m_BalanceMessageLandSale 	= "Paid the Money L${0} for Land.";
		private string m_BalanceMessageRcvLandSale 	= "";
		private string m_BalanceMessageSendGift 	= "Sent Gift L${0} to {1}.";
		private string m_BalanceMessageReceiveGift	= "Received Gift L${0} from {1}.";
		private string m_BalanceMessagePayCharge 	= "";
		private string m_BalanceMessageBuyObject  	= "Bought the Object L${0} from {1}.";
		private string m_BalanceMessageGetMoney  	= "Got the Money L${0} from {1}.";
		private string m_BalanceMessageBuyMoney  	= "Bought the Money L${0}.";
		private string m_BalanceMessageReceiveMoney = "Received L${0} from System.";
		private string m_BalanceMessageRollBack		= "RollBack the Transaction: L${0} from/to {1}.";


		const int MONEYMODULE_REQUEST_TIMEOUT = 30 * 1000;	//30 seconds
		private long TicksToEpoch = new DateTime(1970, 1, 1).Ticks;

		private IMoneyDBService m_moneyDBService;
		private IMoneyServiceCore m_moneyCore;

		protected IConfig m_config;

		/// <value>
		/// Used to notify old regions as to which OpenSim version to upgrade to
		/// </value>
		private string m_opensimVersion;

		private Dictionary<string, string> m_sessionDic;
		private Dictionary<string, string> m_secureSessionDic;
		private Dictionary<string, string> m_webSessionDic;

		protected BaseHttpServer m_httpServer;


		public MoneyXmlRpcModule()
		{
		}


		public void Initialise(string opensimVersion, IConfig config, IMoneyDBService moneyDBService, IMoneyServiceCore moneyCore) 
		{
			m_opensimVersion = opensimVersion;
			m_moneyDBService = moneyDBService;
			m_moneyCore = moneyCore;
			m_config = config;		// [MoneyServer] Section

			m_defaultBalance = m_config.GetInt("DefaultBalance", 1000);

			string ftrans   = m_config.GetString ("enableForceTransfer", "false");
			if (ftrans.ToLower()=="true") m_forceTransfer = true;
			m_forceTransfer = m_config.GetBoolean("EnableForceTransfer", m_forceTransfer);

			string banker  = m_config.GetString("BankerAvatar", "");
			m_bankerAvatar = banker.ToLower();

			string sendmoney  = m_config.GetString ("enableScriptSendMoney", "false");
			if (sendmoney.ToLower()=="true") m_scriptSendMoney = true;
			m_scriptSendMoney = m_config.GetBoolean("EnableScriptSendMoney", m_scriptSendMoney);

			m_scriptAccessKey = m_config.GetString("MoneyScriptAccessKey", "");
			m_scriptIPaddress = m_config.GetString("MoneyScriptIPaddress", "127.0.0.1");

			// クライアント証明書
			m_certFilename = m_config.GetString("ClientCertFilename", "");
			m_certPassword = m_config.GetString("ClientCertPassword", "");
			if (m_certFilename!="") {
				m_cert = new X509Certificate2(m_certFilename, m_certPassword);
				m_log.Info("[MONEY RPC]: Initialise: Issue Authentication of Client. Cert file is " + m_cacertFilename);
			}

			// サーバ認証
			string checkcert = m_config.GetString("CheckServerCert", "false");
			if (checkcert.ToLower()=="true") m_checkServerCert = true;

			m_cacertFilename = m_config.GetString("CACertFilename", "");
			if (m_cacertFilename!="") {
				m_certVerify.SetPrivateCA(m_cacertFilename);
				ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(m_certVerify.ValidateServerCertificate);
				m_log.Info("[MONEY RPC]: Initialise: Execute Authentication of Server. CA file is " + m_cacertFilename);
			}
			else {
				m_checkServerCert = false;
				ServicePointManager.ServerCertificateValidationCallback = null;
			}

			// Update Balance Messages
			m_BalanceMessageLandSale	 = m_config.GetString("BalanceMessageLandSale", 	m_BalanceMessageLandSale);
			m_BalanceMessageRcvLandSale	 = m_config.GetString("BalanceMessageRcvLandSale", 	m_BalanceMessageRcvLandSale);
			m_BalanceMessageSendGift	 = m_config.GetString("BalanceMessageSendGift",		m_BalanceMessageSendGift);
			m_BalanceMessageReceiveGift  = m_config.GetString("BalanceMessageReceiveGift",	m_BalanceMessageReceiveGift);
			m_BalanceMessagePayCharge	 = m_config.GetString("BalanceMessagePayCharge",	m_BalanceMessagePayCharge);
			m_BalanceMessageBuyObject	 = m_config.GetString("BalanceMessageBuyObject", 	m_BalanceMessageBuyObject); 
			m_BalanceMessageGetMoney	 = m_config.GetString("BalanceMessageGetMoney", 	m_BalanceMessageGetMoney); 
			m_BalanceMessageBuyMoney	 = m_config.GetString("BalanceMessageBuyMoney", 	m_BalanceMessageBuyMoney); 
			m_BalanceMessageReceiveMoney = m_config.GetString("BalanceMessageReceiveMoney", m_BalanceMessageReceiveMoney);
			m_BalanceMessageRollBack	 = m_config.GetString("BalanceMessageRollBack", 	m_BalanceMessageRollBack);

			m_sessionDic = m_moneyCore.GetSessionDic();
			m_secureSessionDic = m_moneyCore.GetSecureSessionDic();
			m_webSessionDic = m_moneyCore.GetWebSessionDic();
			RegisterHandlers();
		}


		public void PostInitialise()
		{	
		}


		public void RegisterHandlers()
		{
			m_httpServer = m_moneyCore.GetHttpServer();
			m_httpServer.AddXmlRPCHandler("ClientLogin", handleClientLogin);
			m_httpServer.AddXmlRPCHandler("ClientLogout", handleClientLogout);
			m_httpServer.AddXmlRPCHandler("GetBalance", handleGetBalance);
			m_httpServer.AddXmlRPCHandler("GetTransaction", handleGetTransaction);

			m_httpServer.AddXmlRPCHandler("CancelTransfer", handleCancelTransfer);
			m_httpServer.AddXmlRPCHandler("TransferMoney", handleTransaction);
			m_httpServer.AddXmlRPCHandler("ForceTransferMoney", handleForceTransaction);		// added
			m_httpServer.AddXmlRPCHandler("PayMoneyCharge", handlePayMoneyCharge);				// added
			m_httpServer.AddXmlRPCHandler("AddBankerMoney", handleAddBankerMoney);				// added
			m_httpServer.AddXmlRPCHandler("SendMoneyBalance", handleSendMoneyBalance);			// added

			// this is from original DTL. not check yet.
			m_httpServer.AddXmlRPCHandler("WebLogin", handleWebLogin);
			m_httpServer.AddXmlRPCHandler("WebLogout", handleWebLogout);
			m_httpServer.AddXmlRPCHandler("WebGetBalance", handleWebGetBalance);
			m_httpServer.AddXmlRPCHandler("WebGetTransaction", handleWebGetTransaction);
			m_httpServer.AddXmlRPCHandler("WebGetTransactionNum", handleWebGetTransactionNum);
		}


		//
		//
		//
		public string GetSSLCommonName(XmlRpcRequest request)
		{
			if (request.Params.Count>4) {
				m_sslCommonName = (string)request.Params[4];
			}
			else {
				m_sslCommonName = "";
			}
			return m_sslCommonName;
		}


		//
		public string GetSSLCommonName()
		{
			return m_sslCommonName;
		}




		/// <summary>
		/// Get the user balance when user entering a parcel.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public XmlRpcResponse handleClientLogin(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleClientLogin:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData = new Hashtable();
			string clientUUID = string.Empty;
			string sessionID = string.Empty;
			string secureID = string.Empty;
			string simIP  = string.Empty;
			string avatarName = string.Empty;
			int balance = 0;

			response.Value = responseData;
			if (requestData.ContainsKey("clientUUID")) 			  clientUUID = (string)requestData["clientUUID"];
			if (requestData.ContainsKey("clientSessionID")) 	  sessionID = (string)requestData["clientSessionID"];
			if (requestData.ContainsKey("clientSecureSessionID")) secureID = (string)requestData["clientSecureSessionID"];
			if (requestData.ContainsKey("openSimServIP")) 		  simIP = (string)requestData["openSimServIP"];
			if (requestData.ContainsKey("userName")) 			  avatarName = (string)requestData["userName"];

			//m_log.InfoFormat("[MONEY RPC]: handleClientLogin: client UUID is {0}", clientUUID);

			if (avatarName=="") {
				responseData["success"] = false;
				responseData["clientBalance"] = 0;
				return response;
			}

			//Update the session and secure session dictionary
			lock (m_sessionDic)
			{
				if (!m_sessionDic.ContainsKey(clientUUID))
				{
					m_sessionDic.Add(clientUUID, sessionID);
				}
				else m_sessionDic[clientUUID] = sessionID;
			}

			lock (m_secureSessionDic)
			{
				if (!m_secureSessionDic.ContainsKey(clientUUID))
				{
					m_secureSessionDic.Add(clientUUID, secureID);
				}
				else m_secureSessionDic[clientUUID] = secureID;
			}

			try
			{
				//m_log.InfoFormat("[MONEY RPC]: handleClientLogin: User {0} has logged in, getting balance...", clientUUID);
				balance = m_moneyDBService.getBalance(clientUUID);
				//add user if not exist
				if (balance==-1)
				{
					if (m_moneyDBService.addUser(clientUUID, m_defaultBalance, 0))
					{
						responseData["success"] = true;
						responseData["description"] = "add user successfully";
						responseData["clientBalance"] = m_defaultBalance;
					}
					else
					{
						responseData["success"] = false;
						responseData["description"] = "add user failed";
						responseData["clientBalance"] = 0;
					}
				}
				else if (balance >= 0) //Success
				{
					responseData["success"] = true;
					responseData["description"] = "get user balance successfully";
					responseData["clientBalance"] = balance;
				}
				UserInfo user = new UserInfo();
				user.UserID = clientUUID;
				user.SimIP  = simIP;
				user.Avatar = avatarName;
				//TODO: Add password protection here
				user.PswHash = UUID.Zero.ToString();

				if (!m_moneyDBService.TryAddUserInfo(user))
				{
					m_log.ErrorFormat("[MONEY RPC]: handleClientLogin: Unable to refresh information for user \"{0}\" in DB", avatarName);
					responseData["success"] = false;
					responseData["description"] = "Update or add user information to db failed";
					responseData["clientBalance"] = balance;
				}
				return response;

			}
			catch (Exception e)
			{
				m_log.ErrorFormat("[MONEY RPC]: handleClientLogin: Can't get balance of user {0}", clientUUID);
				responseData["success"] = false;
				responseData["description"] = "Exception occured" + e.ToString();
				responseData["clientBalance"] = 0;
			}
			return response;

		}



		//
		// deleted using ASP.NET  by Fumi.Iseki
		//
		/// <summary>
		/// handle incoming transaction
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public XmlRpcResponse handleTransaction(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleTransaction:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData  = new Hashtable();
			response.Value = responseData;

			int	   amount = 0;
			int	   transactionType = 0;
			string senderID = string.Empty;
			string receiverID = string.Empty;
			string senderSessionID = string.Empty;
			string senderSecureSessionID = string.Empty;
			string objectID = string.Empty;
			string regionHandle = string.Empty;
			string description  = "Newly added on";

			UUID transactionUUID = UUID.Random();

			if (requestData.ContainsKey("senderID")) 			  senderID = (string)requestData["senderID"];
			if (requestData.ContainsKey("receiverID")) 			  receiverID = (string)requestData["receiverID"];
			if (requestData.ContainsKey("senderSessionID")) 	  senderSessionID = (string)requestData["senderSessionID"];
			if (requestData.ContainsKey("senderSecureSessionID")) senderSecureSessionID = (string)requestData["senderSecureSessionID"];
			if (requestData.ContainsKey("amount")) 				  amount = (Int32)requestData["amount"];
			if (requestData.ContainsKey("objectID")) 			  objectID = (string)requestData["objectID"];
			if (requestData.ContainsKey("regionHandle")) 		  regionHandle = (string)requestData["regionHandle"];
			if (requestData.ContainsKey("transactionType")) 	  transactionType = (Int32)requestData["transactionType"];
			if (requestData.ContainsKey("description")) 		  description = (string)requestData["description"];

			if (m_sessionDic.ContainsKey(senderID) && m_secureSessionDic.ContainsKey(senderID))
			{
				if (m_sessionDic[senderID]==senderSessionID && m_secureSessionDic[senderID]==senderSecureSessionID)
				{
					m_log.InfoFormat("[MONEY RPC]: handleTransaction: Transfering money from {0} to {1}", senderID, receiverID);
					int time = (int)((DateTime.Now.Ticks - TicksToEpoch) / 10000000);
					try
					{
						TransactionData transaction = new TransactionData();
						transaction.TransUUID = transactionUUID;
						transaction.Sender   = senderID;
						transaction.Receiver = receiverID;
						transaction.Amount = amount;
						transaction.ObjectUUID = objectID;
						transaction.RegionHandle = regionHandle;
						transaction.Type = transactionType;
						transaction.Time = time;
						transaction.SecureCode = UUID.Random().ToString();
						transaction.Status = (int)Status.PENDING_STATUS;
						transaction.CommonName  = GetSSLCommonName();
						transaction.Description = description + " " + DateTime.Now.ToString();

						UserInfo rcvr = m_moneyDBService.FetchUserInfo(receiverID);
						if (rcvr==null) 
						{
							m_log.ErrorFormat("[MONEY RPC]: handleTransaction: Receive User is not yet in DB {0}", receiverID);
							responseData["success"] = false;
							return response;
						}

						bool result = m_moneyDBService.addTransaction(transaction);
						if (result) 
						{
							UserInfo user = m_moneyDBService.FetchUserInfo(senderID);
							if (user!=null) 
							{
								if (amount!=0)
								{
									string snd_message = "";
									string rcv_message = "";

									if (transaction.Type==(int)TransactionType.Gift) {
										snd_message = m_BalanceMessageSendGift;
										rcv_message = m_BalanceMessageReceiveGift;
									}
									else if (transaction.Type==(int)TransactionType.LandSale) {
										snd_message = m_BalanceMessageLandSale;
										rcv_message = m_BalanceMessageRcvLandSale;
									}
									else if (transaction.Type==(int)TransactionType.PayObject) {
										snd_message = m_BalanceMessageBuyObject;
									}
									else if (transaction.Type==(int)TransactionType.ObjectPays) {		// ObjectGiveMoney
										rcv_message = m_BalanceMessageGetMoney;
									}
						
									responseData["success"] = NotifyTransfer(transactionUUID, snd_message, rcv_message);
								}
								else
								{
									responseData["success"] = true;		// No messages for L$0 object. by Fumi.Iseki
								}
								return response;
							}
						}
						else // add transaction failed
						{
							m_log.ErrorFormat("[MONEY RPC]: handleTransaction: Add transaction for user {0} failed", senderID);
						}

						responseData["success"] = false;
						return response;
					}
					catch (Exception e)
					{
						m_log.Error("[MONEY RPC]: handleTransaction: Exception occurred while adding transaction " + e.ToString());
						responseData["success"] = false;
						return response;
					}
					
				}

			}

			m_log.Error("[MONEY RPC]: handleTransaction: Session authentication failure for sender " + senderID);
			responseData["success"] = false;
			responseData["message"] = "Session check failure, please re-login later!";
			return response;
		}



		//
		// added by Fumi.Iseki
		//
		/// <summary>
		/// handle incoming force transaction. no check senderSessionID and senderSecureSessionID
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public XmlRpcResponse handleForceTransaction(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleForceTransaction:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData  = new Hashtable();
			response.Value = responseData;

			int	   amount = 0;
			int	   transactionType = 0;
			string senderID = string.Empty;
			string receiverID = string.Empty;
			string objectID = string.Empty;
			string regionHandle = string.Empty;
			string description  = "Newly added on";

			UUID transactionUUID = UUID.Random();

			//
			if (!m_forceTransfer)
			{
				m_log.Error("[MONEY RPC]: handleForceTransaction: Not allowed force transfer of Money. Set enableForceTransfer at [MoneyServer] to true in MoneyServer.ini");
				responseData["success"] = false;
				responseData["message"] = "not allowed force transfer of Money!";
				return response;
			}

			if (requestData.ContainsKey("senderID")) 			senderID = (string)requestData["senderID"];
			if (requestData.ContainsKey("receiverID")) 			receiverID = (string)requestData["receiverID"];
			if (requestData.ContainsKey("amount")) 				amount = (Int32)requestData["amount"];
			if (requestData.ContainsKey("objectID")) 			objectID = (string)requestData["objectID"];
			if (requestData.ContainsKey("regionHandle")) 		regionHandle = (string)requestData["regionHandle"];
			if (requestData.ContainsKey("transactionType")) 	transactionType = (Int32)requestData["transactionType"];
			if (requestData.ContainsKey("description")) 		description = (string)requestData["description"];

			m_log.InfoFormat("[MONEY RPC]: handleForceTransaction: Force transfering money from {0} to {1}", senderID, receiverID);
			int time = (int)((DateTime.Now.Ticks - TicksToEpoch) / 10000000);

			try
			{
				TransactionData transaction = new TransactionData();
				transaction.TransUUID = transactionUUID;
				transaction.Sender   = senderID;
				transaction.Receiver = receiverID;
				transaction.Amount = amount;
				transaction.ObjectUUID = objectID;
				transaction.RegionHandle = regionHandle;
				transaction.Type = transactionType;
				transaction.Time = time;
				transaction.SecureCode = UUID.Random().ToString();
				transaction.Status = (int)Status.PENDING_STATUS;
				transaction.CommonName  = GetSSLCommonName();
				transaction.Description = description + " " + DateTime.Now.ToString();

				UserInfo rcvr = m_moneyDBService.FetchUserInfo(receiverID);
				if (rcvr==null) 
				{
					m_log.ErrorFormat("[MONEY RPC]: handleForceTransaction: Force receive User is not yet in DB {0}", receiverID);
					responseData["success"] = false;
					return response;
				}

				bool result = m_moneyDBService.addTransaction(transaction);
				if (result) 
				{
					UserInfo user = m_moneyDBService.FetchUserInfo(senderID);
					if (user!=null) 
					{
						if (amount!=0)
						{
							string snd_message = "";
							string rcv_message = "";

							if (transaction.Type==(int)TransactionType.Gift) {
								snd_message = m_BalanceMessageSendGift;
								rcv_message = m_BalanceMessageReceiveGift;
							}
							else if (transaction.Type==(int)TransactionType.LandSale) {
								snd_message = m_BalanceMessageLandSale;
								snd_message = m_BalanceMessageRcvLandSale;
							}
							else if (transaction.Type==(int)TransactionType.PayObject) {
								snd_message = m_BalanceMessageBuyObject;
							}
							else if (transaction.Type==(int)TransactionType.ObjectPays) {		// ObjectGiveMoney
								rcv_message = m_BalanceMessageGetMoney;
							}
						
							responseData["success"] = NotifyTransfer(transactionUUID, snd_message, rcv_message);
						}
						else
						{
							responseData["success"] = true;		// No messages for L$0 object. by Fumi.Iseki
						}
						return response;
					}
				}
				else // add transaction failed
				{
					m_log.ErrorFormat("[MONEY RPC]: handleForceTransaction: Add force transaction for user {0} failed", senderID);
				}

				responseData["success"] = false;
				return response;
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY RPC]: handleForceTransaction: Exception occurred while adding force transaction " + e.ToString());
				responseData["success"] = false;
				return response;
			}
		}



		//
		// added by Fumi.Iseki
		//
		/// <summary>
		/// handle adding money transaction.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public XmlRpcResponse handleAddBankerMoney(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleAddBankerMoney:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData  = new Hashtable();
			response.Value = responseData;

			int	   amount = 0;
			int	   transactionType = 0;
			string senderID = UUID.Zero.ToString();
			string bankerID = string.Empty;
			string regionHandle = "0";
			string description  = "Add Money to Avatar on";

			UUID transactionUUID = UUID.Random();

			responseData["success"] = false;

			if (requestData.ContainsKey("bankerID")) 			bankerID = (string)requestData["bankerID"];
			if (requestData.ContainsKey("amount")) 				amount = (Int32)requestData["amount"];
			if (requestData.ContainsKey("regionHandle")) 		regionHandle = (string)requestData["regionHandle"];
			if (requestData.ContainsKey("transactionType")) 	transactionType = (Int32)requestData["transactionType"];
			if (requestData.ContainsKey("description")) 		description = (string)requestData["description"];

			// Check Banker Avatar
			if (m_bankerAvatar!=UUID.Zero.ToString() && m_bankerAvatar!=bankerID)
			{
				m_log.Error("[MONEY RPC]: handleAddBankerMoney: Not allowed add money to avatar!!");
				m_log.Error("[MONEY RPC]: handleAddBankerMoney: Set BankerAvatar at [MoneyServer] in MoneyServer.ini");
				responseData["message"] = "not allowed add money to avatar!";
				responseData["banker"]  = false;
				return response;
			}
			responseData["banker"] = true;

			m_log.InfoFormat("[MONEY RPC]: handleAddBankerMoney: Add money to avatar {0}", bankerID);
			int time = (int)((DateTime.Now.Ticks - TicksToEpoch) / 10000000);

			try
			{
				TransactionData transaction = new TransactionData();
				transaction.TransUUID = transactionUUID;
				transaction.Sender   = senderID;
				transaction.Receiver = bankerID;
				transaction.Amount = amount;
				transaction.ObjectUUID   = UUID.Zero.ToString();
				transaction.RegionHandle = regionHandle;
				transaction.Type = transactionType;
				transaction.Time = time;
				transaction.SecureCode = UUID.Random().ToString();
				transaction.Status = (int)Status.PENDING_STATUS;
				transaction.CommonName  = GetSSLCommonName();
				transaction.Description = description + " " + DateTime.Now.ToString();

				UserInfo rcvr = m_moneyDBService.FetchUserInfo(bankerID);
				if (rcvr==null) 
				{
					m_log.ErrorFormat("[MONEY RPC]: handleAddBankerMoney: Avatar is not yet in DB {0}", bankerID);
					return response;
				}

				bool result = m_moneyDBService.addTransaction(transaction);
				if (result) 
				{
					if (amount!=0)
					{
						if (m_moneyDBService.DoAddMoney(transactionUUID))
						{
							transaction = m_moneyDBService.FetchTransaction(transactionUUID);
							if (transaction!=null && transaction.Status==(int)Status.SUCCESS_STATUS)
							{
								m_log.InfoFormat("[MONEY RPC]: handleAddBankerMoney: Adding money finished successfully, now update balance: {0}", 
																															transactionUUID.ToString());
								string message = string.Format(m_BalanceMessageBuyMoney, amount, "SYSTEM");
								UpdateBalance(transaction.Receiver, message);
								responseData["success"] = true;
							}
						}
					}
					else
					{
						responseData["success"] = true;		// No messages for L$0 add
					}
					return response;
				}
				else // add transaction failed
				{
					m_log.ErrorFormat("[MONEY RPC]: handleAddBankerMoney: Add force transaction for user {0} failed", senderID);
				}

				return response;
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY RPC]: handleAddBankerMoney: Exception occurred while adding money transaction " + e.ToString());
				return response;
			}
		}



		//
		// added by Fumi.Iseki
		//
		/// <summary>
		/// handle sending money transaction.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public XmlRpcResponse handleSendMoneyBalance(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleSendMoneyBalance:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData  = new Hashtable();
			response.Value = responseData;

			int	   amount = 0;
			int	   transactionType = 0;
			string senderID = UUID.Zero.ToString();
			string avatarID = string.Empty;
			string description  = "Send Money to Avatar on";
			string clientIP   = remoteClient.Address.ToString();
			string secretCode = string.Empty;

			UUID transactionUUID = UUID.Random();

			responseData["success"] = false;
			
			if (!m_scriptSendMoney || m_scriptAccessKey=="")
			{
				m_log.Error("[MONEY RPC]: handleSendMoneyBalance: Not allowed send money to avatar!!");
				m_log.Error("[MONEY RPC]: handleSendMoneyBalance: Set enableScriptSendMoney and MoneyScriptAccessKey at [MoneyServer] in MoneyServer.ini");
				responseData["message"] = "not allowed set money to avatar!";
				return response;
			}

			if (requestData.ContainsKey("avatarID")) 		 avatarID = (string)requestData["avatarID"];
			if (requestData.ContainsKey("amount")) 			 amount = (Int32)requestData["amount"];
			if (requestData.ContainsKey("transactionType"))  transactionType = (Int32)requestData["transactionType"];
			if (requestData.ContainsKey("description")) 	 description = (string)requestData["description"];
			if (requestData.ContainsKey("secretAccessCode")) secretCode = (string)requestData["secretAccessCode"];

			MD5 md5 = MD5.Create();
			byte[] code = md5.ComputeHash(ASCIIEncoding.Default.GetBytes(m_scriptAccessKey + "_" + clientIP));
			string hash = BitConverter.ToString(code).ToLower().Replace("-", "");
			code = md5.ComputeHash(ASCIIEncoding.Default.GetBytes(hash + "_" + m_scriptIPaddress));
			hash = BitConverter.ToString(code).ToLower().Replace("-", "");

			if (secretCode.ToLower()!=hash)
			{
				m_log.Error("[MONEY RPC]: handleSendMoneyBalance: Not allowed send money to avatar!!");
				m_log.Error("[MONEY RPC]: handleSendMoneyBalance: Not match Script Key");
				responseData["message"] = "not allowed send money to avatar! not match Script Key";
				return response;
			}

			m_log.InfoFormat("[MONEY RPC]: handleSendMoneyBalance: Send money to avatar {0}", avatarID);
			int time = (int)((DateTime.Now.Ticks - TicksToEpoch) / 10000000);

			try
			{
				TransactionData transaction = new TransactionData();
				transaction.TransUUID = transactionUUID;
				transaction.Sender   = senderID;
				transaction.Receiver = avatarID;
				transaction.Amount = amount;
				transaction.ObjectUUID   = UUID.Zero.ToString();
				transaction.RegionHandle = "0";
				transaction.Type = transactionType;
				transaction.Time = time;
				transaction.SecureCode = UUID.Random().ToString();
				transaction.Status = (int)Status.PENDING_STATUS;
				transaction.CommonName  = GetSSLCommonName();
				transaction.Description = description + " " + DateTime.Now.ToString();

				UserInfo rcvr = m_moneyDBService.FetchUserInfo(avatarID);
				if (rcvr==null) 
				{
					m_log.ErrorFormat("[MONEY RPC]: handleSendMoneyBalance: Avatar is not yet in DB: {0}", avatarID);
					return response;
				}

				bool result = m_moneyDBService.addTransaction(transaction);
				if (result) 
				{
					if (amount!=0)
					{
						if (m_moneyDBService.DoAddMoney(transactionUUID))
						{
							transaction = m_moneyDBService.FetchTransaction(transactionUUID);
							if (transaction!=null && transaction.Status==(int)Status.SUCCESS_STATUS)
							{
								m_log.InfoFormat("[MONEY RPC]: handleSendMoneyBalance: Sending money finished successfully, now update balance {0}", 
																															transactionUUID.ToString());
								string message = string.Format(m_BalanceMessageReceiveMoney, amount, "SYSTEM");
								UpdateBalance(transaction.Receiver, message);
								responseData["success"] = true;
							}
						}
					}
					else
					{
						responseData["success"] = true;		// No messages for L$0 add
					}
					return response;
				}
				else // add transaction failed
				{
					m_log.ErrorFormat("[MONEY RPC]: handleSendMoneyBalance: Add force transaction for user {0} failed", senderID);
				}

				return response;
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY RPC]: handleSendMoneyBalance: Exception occurred while adding money transaction " + e.ToString());
				return response;
			}
		}



		//
		// added by Fumi.Iseki
		//
		/// <summary>
		/// handle pay charge transaction. no check receiver information.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public XmlRpcResponse handlePayMoneyCharge(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handlePayMoneyCharge:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData  = new Hashtable();
			response.Value = responseData;

			int	   amount = 0;
			int	   transactionType = 0;
			string senderID = string.Empty;
			string receiverID = UUID.Zero.ToString();
			string senderSessionID = string.Empty;
			string senderSecureSessionID = string.Empty;
			string objectID = UUID.Zero.ToString();
			string regionHandle = string.Empty;
			string description  = "Pay Charge on";

			UUID transactionUUID = UUID.Random();

			if (requestData.ContainsKey("senderID")) 			  senderID = (string)requestData["senderID"];
			if (requestData.ContainsKey("senderSessionID")) 	  senderSessionID = (string)requestData["senderSessionID"];
			if (requestData.ContainsKey("senderSecureSessionID")) senderSecureSessionID = (string)requestData["senderSecureSessionID"];
			if (requestData.ContainsKey("amount")) 				  amount = (Int32)requestData["amount"];
			if (requestData.ContainsKey("regionHandle")) 		  regionHandle = (string)requestData["regionHandle"];
			if (requestData.ContainsKey("transactionType")) 	  transactionType = (Int32)requestData["transactionType"];
			if (requestData.ContainsKey("description")) 		  description = (string)requestData["description"];

			if (m_sessionDic.ContainsKey(senderID) && m_secureSessionDic.ContainsKey(senderID))
			{
				if (m_sessionDic[senderID]==senderSessionID && m_secureSessionDic[senderID]==senderSecureSessionID)
				{
					m_log.InfoFormat("[MONEY RPC]: handlePayMoneyCharge: Pay from {0}", senderID);
					int time = (int)((DateTime.Now.Ticks - TicksToEpoch) / 10000000);
					try
					{
						TransactionData transaction = new TransactionData();
						transaction.TransUUID = transactionUUID;
						transaction.Sender   = senderID;
						transaction.Receiver = receiverID;
						transaction.Amount = amount;
						transaction.ObjectUUID = objectID;
						transaction.RegionHandle = regionHandle;
						transaction.Type = transactionType;
						transaction.Time = time;
						transaction.SecureCode = UUID.Random().ToString();
						transaction.Status = (int)Status.PENDING_STATUS;
						transaction.CommonName  = GetSSLCommonName();
						transaction.Description = description + " " + DateTime.Now.ToString();

						bool result = m_moneyDBService.addTransaction(transaction);
						if (result) 
						{
							UserInfo user = m_moneyDBService.FetchUserInfo(senderID);
							if (user!=null) 
							{
								if (amount!=0)
								{
									string message = string.Format(m_BalanceMessagePayCharge, amount, "SYSTEM");
									responseData["success"] = NotifyTransfer(transactionUUID, message, "");
								}
								else
								{
									responseData["success"] = true;		// No messages for L$0 object. by Fumi.Iseki
								}
								return response;
							}
						}
						else // add transaction failed
						{
							m_log.ErrorFormat("[MONEY RPC]: handlePayMoneyCharge: Pay money transaction for user {0} failed", senderID);
						}

						responseData["success"] = false;
						return response;
					}
					catch (Exception e)
					{
						m_log.Error("[MONEY RPC]: handlePayMoneyCharge: Exception occurred while pay money transaction " + e.ToString());
						responseData["success"] = false;
						return response;
					}
					
				}

			}

			m_log.Error("[MONEY RPC]: handlePayMoneyCharge: Session authentication failure for sender " + senderID);
			responseData["success"] = false;
			responseData["message"] = "Session check failure, please re-login later!";
			return response;
		}



		//
		//  added by Fumi.Iseki
		//
		/// <summary>
		/// Continue transaction with no confirm.
		/// </summary>
		/// <param name="transactionUUID"></param>
		/// <returns></returns>
		public bool  NotifyTransfer(UUID transactionUUID, string msg2sender, string msg2receiver)
		{
			//m_log.InfoFormat("[MONEY RPC]: NotifyTransfer: User has accepted the transaction, now continue with the transaction");

			try
			{
				if (m_moneyDBService.DoTransfer(transactionUUID))
				{
					TransactionData transaction = m_moneyDBService.FetchTransaction(transactionUUID);
					if (transaction!=null && transaction.Status==(int)Status.SUCCESS_STATUS)
					{
						//m_log.InfoFormat("[MONEY RPC]: NotifyTransfer: Transaction Type = {0}", transaction.Type);
						//m_log.InfoFormat("[MONEY RPC]: NotifyTransfer: Payment finished successfully, now update balance {0}", transactionUUID.ToString());

						bool updateSender = true;
						bool updateReceiv = true;
						if (transaction.Sender==transaction.Receiver) updateSender = false;
						//if (transaction.Type==(int)TransactionType.UploadCharge) return true;
						if (transaction.Type==(int)TransactionType.UploadCharge) updateReceiv = false;

						if (updateSender) {
							UserInfo receiverInfo = m_moneyDBService.FetchUserInfo(transaction.Receiver);
							string receiverName = "unknown user";
							if (receiverInfo!=null) receiverName = receiverInfo.Avatar;
							string snd_message = string.Format(msg2sender, transaction.Amount, receiverName);
							UpdateBalance(transaction.Sender, snd_message);
						}
						if (updateReceiv) {
							UserInfo senderInfo = m_moneyDBService.FetchUserInfo(transaction.Sender);
							string senderName = "unknown user";
							if (senderInfo!=null) senderName = senderInfo.Avatar;
							string rcv_message = string.Format(msg2receiver, transaction.Amount, senderName);
							UpdateBalance(transaction.Receiver, rcv_message);
						}

						// Notify to sender?
						if (transaction.Type==(int)TransactionType.PayObject)
						{
							//m_log.InfoFormat("[MONEY RPC]: NotifyTransfer: Now notify opensim to give object to customer {0} ", transaction.Sender);
							Hashtable requestTable = new Hashtable();
							requestTable["clientUUID"]   = transaction.Sender;
							requestTable["receiverUUID"] = transaction.Receiver;

							if(m_sessionDic.ContainsKey(transaction.Sender)&&m_secureSessionDic.ContainsKey(transaction.Sender))
							{
								requestTable["clientSessionID"] = m_sessionDic[transaction.Sender];
								requestTable["clientSecureSessionID"] = m_secureSessionDic[transaction.Sender];
							}
							else 
							{
								requestTable["clientSessionID"] =  UUID.Zero.ToString();
								requestTable["clientSecureSessionID"] = UUID.Zero.ToString();
							}
							requestTable["transactionType"] = transaction.Type;
							requestTable["amount"] = transaction.Amount;
							requestTable["objectID"] = transaction.ObjectUUID;
							requestTable["regionHandle"] = transaction.RegionHandle;

							UserInfo user = m_moneyDBService.FetchUserInfo(transaction.Sender);
							if (user!=null)
							{
								Hashtable responseTable = genericCurrencyXMLRPCRequest(requestTable, "OnMoneyTransfered", user.SimIP);

								if (responseTable!=null && responseTable.ContainsKey("success"))
								{
									//User not online or failed to get object ?
									if (!(bool)responseTable["success"])
									{
										m_log.ErrorFormat("[MONEY RPC]: NotifyTransfer: User {0} can't get the object, rolling back", transaction.Sender);
										if (RollBackTransaction(transaction))
										{
											m_log.ErrorFormat("[MONEY RPC]: NotifyTransfer: Transaction {0} failed but roll back succeeded", transactionUUID.ToString());
										}
										else
										{
											m_log.ErrorFormat("[MONEY RPC]: NotifyTransfer: Transaction {0} failed and roll back failed as well", 
																														transactionUUID.ToString());
										}
									}
									else
									{
										m_log.InfoFormat("[MONEY RPC]: NotifyTransfer: Transaction {0} finished successfully", transactionUUID.ToString());
										return true;
									}
								}
							}
							return false;
						}
						return true;
					}
					
				}
				m_log.ErrorFormat("[MONEY RPC]: NotifyTransfer: Transaction {0} failed", transactionUUID.ToString());
			}
			catch (Exception e)
			{
				m_log.ErrorFormat("[MONEY RPC]: NotifyTransfer: exception occurred when transaction {0}: {1}", transactionUUID.ToString(), e.ToString());
			}
			return false;
		}



		/// <summary>
		/// Get the user balance.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public XmlRpcResponse handleGetBalance(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleGetBalance:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData = new Hashtable();
			response.Value = responseData;
			string clientUUID = string.Empty;
			string sessionID = string.Empty;
			string secureID = string.Empty;
			int balance;

			if (requestData.ContainsKey("clientUUID"))			  clientUUID = (string)requestData["clientUUID"];
			if (requestData.ContainsKey("clientSessionID")) 	  sessionID = (string)requestData["clientSessionID"];
			if (requestData.ContainsKey("clientSecureSessionID")) secureID = (string)requestData["clientSecureSessionID"];

			m_log.InfoFormat("[MONEY RPC]: handleGetBalance: Getting balance for user {0}", clientUUID);
			if (m_sessionDic.ContainsKey(clientUUID) && m_secureSessionDic.ContainsKey(clientUUID))
			{
				if (m_sessionDic[clientUUID]==sessionID && m_secureSessionDic[clientUUID]==secureID)
				{
					try
					{
						balance = m_moneyDBService.getBalance(clientUUID);

						if (balance==-1) // User not found
						{
							if (m_moneyDBService.addUser(clientUUID, m_defaultBalance, 0))
							{
								responseData["success"] = true;
								responseData["description"] = "add user successfully";
								responseData["clientBalance"] = m_defaultBalance;
							}
							else
							{
								responseData["success"] = false;
								responseData["description"] = "add user failed";
								responseData["clientBalance"] = 0;
							}
						}

						else if (balance >= 0)
						{
							responseData["success"] = true;
							responseData["clientBalance"] = balance;
						}
						return response;
					}
					catch (Exception e)
					{
						m_log.ErrorFormat("[MONEY RPC]: handleGetBalance: Can't get balance for user {0}, Exception {1}", clientUUID, e.ToString());
					}

				}
			}

			m_log.Error("[MONEY RPC]: handleGetBalance: Session authentication failed when getting balance for user " + clientUUID);

			responseData["success"] = false;
			responseData["description"] = "Session check failure, please re-login";
			return response;
		}



		public XmlRpcResponse handleClientLogout(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleClientLogout:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData = new Hashtable();
			string clientUUID = string.Empty;

			response.Value = responseData;
			if (requestData.ContainsKey("clientUUID")) clientUUID = (string)requestData["clientUUID"];

			//m_log.InfoFormat("[MONEY RPC]: handleClientLogout: User {0} is logging off", clientUUID);
			try
			{
				lock (m_sessionDic)
				{
					if (m_sessionDic.ContainsKey(clientUUID))
					{
						m_sessionDic.Remove(clientUUID);
					}
				}

				lock (m_secureSessionDic)
				{
					if (m_secureSessionDic.ContainsKey(clientUUID))
					{
						m_secureSessionDic.Remove(clientUUID);
					}
				}
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY RPC]: handleClientLogout: Failed to delete user session " + e.ToString() );
				responseData["success"] = false;
			}
			responseData["success"] = true;

			return response;
		}



		/// <summary>   
		/// Generic XMLRPC client abstraction
		/// </summary>   
		/// <param name="ReqParams">Hashtable containing parameters to the method</param>   
		/// <param name="method">Method to invoke</param>   
		/// <returns>Hashtable with success=>bool and other values</returns>   
		private Hashtable genericCurrencyXMLRPCRequest(Hashtable reqParams, string method, string uri)
		{
			m_log.InfoFormat("[MONEY RPC]: genericCurrencyXMLRPCRequest: to {0}", uri);

			if (reqParams.Count<=0 || string.IsNullOrEmpty(method)) return null;

			if (m_checkServerCert)
			{
				if (!uri.StartsWith("https://")) 
				{
					m_log.InfoFormat("[MONEY RPC]: genericCurrencyXMLRPCRequest: CheckServerCert is true, but protocol is not HTTPS. Please check INI file");
					//return null; 
				}
			}
			else
			{
				if (!uri.StartsWith("https://") && !uri.StartsWith("http://"))
				{
					m_log.ErrorFormat("[MONEY RPC]: genericCurrencyXMLRPCRequest: Invalid Region Server URL: {0}", uri);
					return null; 
				}
			}


			ArrayList arrayParams = new ArrayList();
			arrayParams.Add(reqParams);
			XmlRpcResponse moneyServResp = null;
			try
			{
				//XmlRpcRequest moneyModuleReq = new XmlRpcRequest(method, arrayParams);
				//moneyServResp = moneyModuleReq.Send(uri, MONEYMODULE_REQUEST_TIMEOUT);
				NSLXmlRpcRequest moneyModuleReq = new NSLXmlRpcRequest(method, arrayParams);
				moneyServResp = moneyModuleReq.certSend(uri, m_cert, m_checkServerCert, MONEYMODULE_REQUEST_TIMEOUT);
			}
			catch (Exception ex)
			{
				m_log.ErrorFormat("[MONEY RPC]: genericCurrencyXMLRPCRequest: Unable to connect to Region Server {0}", uri);
				m_log.ErrorFormat("[MONEY RPC]: genericCurrencyXMLRPCRequest: {0}", ex.ToString());

				Hashtable ErrorHash = new Hashtable();
				ErrorHash["success"] = false;
				ErrorHash["errorMessage"] = "Failed to perform actions on OpenSim Server";
				ErrorHash["errorURI"] = "";
				return ErrorHash;
			}

			if (moneyServResp.IsFault)
			{
				Hashtable ErrorHash = new Hashtable();
				ErrorHash["success"] = false;
				ErrorHash["errorMessage"] = "Failed to perform actions on OpenSim Server";
				ErrorHash["errorURI"] = "";
				return ErrorHash;
			}
			Hashtable moneyRespData = (Hashtable)moneyServResp.Value;

			return moneyRespData;
		}



		/// <summary>
		/// Update the client balance.We don't care about the result.
		/// </summary>
		/// <param name="userID"></param>
		private void UpdateBalance(string userID, string message)
		{
			//m_log.InfoFormat("[MONEY RPC]: UpdateBalance: ID = {0}, Message = {1}", userID, message);

			string sessionID  = string.Empty;
			string secureID   = string.Empty;

			if (m_sessionDic.ContainsKey(userID) && m_secureSessionDic.ContainsKey(userID))
			{
				sessionID  = m_sessionDic[userID];
				secureID   = m_secureSessionDic[userID];

				Hashtable requestTable = new Hashtable();
				requestTable["clientUUID"] = userID;
				requestTable["clientSessionID"] = sessionID;
				requestTable["clientSecureSessionID"] = secureID;
				requestTable["Balance"] = m_moneyDBService.getBalance(userID);
				if (message!="") requestTable["Message"] = message;

				UserInfo user = m_moneyDBService.FetchUserInfo(userID);
				if (user!=null) {
					genericCurrencyXMLRPCRequest(requestTable, "UpdateBalance", user.SimIP);
					//m_log.InfoFormat("[MONEY RPC]: UpdateBalance: Sended UpdateBalance Request to {0}", user.SimIP.ToString());
				}
			}
		}



		/// <summary>
		/// RollBack the transaction if user failed to get the object paid
		/// </summary>
		/// <param name="transaction"></param>
		/// <returns></returns>
		protected bool RollBackTransaction(TransactionData transaction)
		{
			//m_log.InfoFormat("[MONEY RPC]: RollBackTransaction:");

			if(m_moneyDBService.withdrawMoney(transaction.TransUUID, transaction.Receiver, transaction.Amount))
			{
				if(m_moneyDBService.giveMoney(transaction.TransUUID, transaction.Sender, transaction.Amount))
				{
					m_log.InfoFormat("[MONEY RPC]: RollBackTransaction: Transaction {0} successfully", transaction.TransUUID.ToString());
					m_moneyDBService.updateTransactionStatus(transaction.TransUUID, (int)Status.FAILED_STATUS, 
																	"The buyer failed to get the object, roll back the transaction");

					UserInfo senderInfo   = m_moneyDBService.FetchUserInfo(transaction.Sender);
					UserInfo receiverInfo = m_moneyDBService.FetchUserInfo(transaction.Receiver);
					string senderName 	= "unknown user";
					string receiverName = "unknown user";
					if (senderInfo!=null)   senderName   = senderInfo.Avatar;
					if (receiverInfo!=null) receiverName = receiverInfo.Avatar;

					string snd_message = string.Format(m_BalanceMessageRollBack, transaction.Amount, receiverName);
					string rcv_message = string.Format(m_BalanceMessageRollBack, transaction.Amount, senderName);

					if (transaction.Sender!=transaction.Receiver) UpdateBalance(transaction.Sender, snd_message);
					UpdateBalance(transaction.Receiver, rcv_message);
					return true;
				}
			}
			return false;
		}



		//
		public XmlRpcResponse handleCancelTransfer(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleCancelTransfer:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData = new Hashtable();

			string secureCode = string.Empty;
			string transactionID = string.Empty;
			UUID transactionUUID = UUID.Zero;

			response.Value = responseData;

			if (requestData.ContainsKey("secureCode")) secureCode = (string)requestData["secureCode"];
			if (requestData.ContainsKey("transactionID"))
			{
				transactionID = (string)requestData["transactionID"];
				UUID.TryParse(transactionID, out transactionUUID);
			}

			if (string.IsNullOrEmpty(secureCode) || string.IsNullOrEmpty(transactionID))
			{
				responseData["success"] = false;
				m_log.Error("[MONEY RPC]: handleCancelTransfer: secureCode or transactionID can't be empty");
				return response;
			}

			TransactionData transaction = m_moneyDBService.FetchTransaction(transactionUUID);
			UserInfo user = m_moneyDBService.FetchUserInfo(transaction.Sender);
		 
			try
			{
				m_log.InfoFormat("[MONEY RPC]: handleCancelTransfer: User {0} wanted to cancel the transaction", user.Avatar);
				if (m_moneyDBService.ValidateTransfer(secureCode, transactionUUID))
				{
					m_log.InfoFormat("[MONEY RPC]: handleCancelTransfer: User {0} has canceled the transaction {1}", user.Avatar, transactionID);
					m_moneyDBService.updateTransactionStatus(transactionUUID, (int)Status.FAILED_STATUS, 
															"User canceled the transaction on " + DateTime.Now.ToString());
					responseData["success"] = true;
				}
			}
			catch (Exception e)
			{
				m_log.ErrorFormat("[MONEY RPC]: handleCancelTransfer: Exception occurred when transaction {0}: {1}", transactionID, e.ToString());
				responseData["success"] = false;
			}
			return response;
		}



		//
		public XmlRpcResponse handleGetTransaction(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleGetTransaction:");

			GetSSLCommonName(request);

			Hashtable requestData   = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData  = new Hashtable();
			response.Value = responseData;

			string clientID = string.Empty;
			string sessionID = string.Empty;
			string secureID = string.Empty;
			string transactionID = string.Empty;
			UUID transactionUUID = UUID.Zero;

			if (requestData.ContainsKey("clientUUID"))			  clientID 	= (string)requestData["clientUUID"];
			if (requestData.ContainsKey("clientSessionID")) 	  sessionID = (string)requestData["clientSessionID"];
			if (requestData.ContainsKey("clientSecureSessionID")) secureID  = (string)requestData["clientSecureSessionID"];

			if (requestData.ContainsKey("transactionID"))
			{
				transactionID = (string)requestData["transactionID"];
				UUID.TryParse(transactionID, out transactionUUID);
			}

			if (m_sessionDic.ContainsKey(clientID) && m_secureSessionDic.ContainsKey(clientID))
			{
				if (m_sessionDic[clientID]==sessionID && m_secureSessionDic[clientID]==secureID)
				{
					if (string.IsNullOrEmpty(transactionID))
					{
						responseData["success"] = false;
						responseData["description"] = "TransactionID is empty";
						m_log.Error("[MONEY RPC]: handleGetTransaction: TransactionID is empty");
						return response;
					}

					try {
						TransactionData transaction = m_moneyDBService.FetchTransaction(transactionUUID);
						if (transaction!=null)
						{
							responseData["success"]  = true;
							responseData["amount"] 	 = transaction.Amount;
							responseData["time"]   	 = transaction.Time;
							responseData["type"]   	 = transaction.Type;
							responseData["sender"]   = transaction.Sender.ToString();
							responseData["receiver"] = transaction.Receiver.ToString();
							responseData["description"]	= transaction.Description;
						}
						else
						{
							responseData["success"] = false;
							responseData["description"] = "Invalid Transaction UUID";
						}

						return response;
					}

					catch (Exception e)
					{
						m_log.ErrorFormat("[MONEY RPC]: handleGetTransaction: {0}", e.ToString());
						m_log.ErrorFormat("[MONEY RPC]: handleGetTransaction: Can't get transaction information for {0}", transactionUUID.ToString());
					}

				}
			}

			responseData["success"] = false;
			responseData["description"] = "Session check failure, please re-login";

			return response;
		}



		//
		public XmlRpcResponse handleWebLogin(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleWebLogin:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData = new Hashtable();
			string userID = string.Empty;
			string webSessionID = string.Empty;


			response.Value = responseData;

			if (requestData.ContainsKey("userID")) 	  userID = (string)requestData["userID"];
			if (requestData.ContainsKey("sessionID")) webSessionID = (string)requestData["sessionID"];

			if (string.IsNullOrEmpty(userID) || string.IsNullOrEmpty(webSessionID))
			{
				responseData["success"] = false;
				responseData["errorMessage"] = "userID or sessionID can`t be empty, login failed!";
				return response;
			}

			//Update the web session dictionary
			lock (m_webSessionDic)
			{
				if (!m_webSessionDic.ContainsKey(userID))
				{
					m_webSessionDic.Add(userID, webSessionID);
				}
				else m_webSessionDic[userID] = webSessionID;
			}

			m_log.InfoFormat("[MONEY RPC]: handleWebLogin: User {0} has logged in from web", userID);
			responseData["success"] = true;
			return response;
		}



		//
		public XmlRpcResponse handleWebLogout(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleWebLogout:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData = new Hashtable();
			string userID = string.Empty;
			string webSessionID = string.Empty;

			response.Value = responseData;

			if (requestData.ContainsKey("userID")) 	  userID = (string)requestData["userID"];
			if (requestData.ContainsKey("sessionID")) webSessionID = (string)requestData["sessionID"];

			if (string.IsNullOrEmpty(userID) || string.IsNullOrEmpty(webSessionID))
			{
				responseData["success"] = false;
				responseData["errorMessage"] = "userID or sessionID can`t be empty, log out failed!";
				return response;
			}

			//Update the web session dictionary
			lock (m_webSessionDic)
			{
				if (m_webSessionDic.ContainsKey(userID))
				{
					m_webSessionDic.Remove(userID);
				}
			}

			m_log.InfoFormat("[MONEY RPC]: handleWebLogout: User {0} has logged out from web", userID);
			responseData["success"] = true;
			return response;

		}



		/// <summary>
		/// Get balance method for web pages.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public XmlRpcResponse handleWebGetBalance(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleWebGetBalance:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData = new Hashtable();
			string userID = string.Empty;
			string webSessionID = string.Empty;
			int balance = 0;

			response.Value = responseData;

			if (requestData.ContainsKey("userID")) 	  userID = (string)requestData["userID"];
			if (requestData.ContainsKey("sessionID")) webSessionID = (string)requestData["sessionID"];
			
			m_log.InfoFormat("[MONEY RPC]: handleWebGetBalance: Getting balance for user {0}", userID);

			if (m_webSessionDic.ContainsKey(userID)) //perform session check
			{
				if (m_webSessionDic[userID]==webSessionID)
				{
					try
					{
						balance = m_moneyDBService.getBalance(userID);
						UserInfo user = m_moneyDBService.FetchUserInfo(userID);
						if (user!=null)
						{
							responseData["userName"] = user.Avatar;
						}
						else
						{
							responseData["userName"] = "unknown user";
						}
						if (balance==-1) // User not found
						{
							responseData["success"] = false;
							responseData["errorMessage"] = "User not found";
							responseData["balance"] = 0;
						}

						else if (balance >= 0)
						{
							responseData["success"] = true;
							responseData["balance"] = balance;
						}
						return response;
					}
					catch (Exception e)
					{
						m_log.ErrorFormat("[MONEY RPC]: handleWebGetBalance: Can't get balance for user {0}, Exception {1}", userID, e.ToString());
						responseData["success"] = false;
						responseData["errorMessage"] = "Exception occurred when getting balance";
						return response;
					}

				}
			}

			m_log.Error("[MONEY RPC]: handleWebLogout: Session authentication failed when getting balance for user " + userID);

			responseData["success"] = false;
			responseData["errorMessage"] = "Session check failure, please re-login";
			return response;
		}



		/// <summary>
		/// Get transaction for web pages
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public XmlRpcResponse handleWebGetTransaction(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleWebGetTransaction:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData = new Hashtable();
			string userID = string.Empty;
			string webSessionID = string.Empty;
			int lastIndex = -1;
			int startTime = 0;
			int endTime = 0;

			response.Value = responseData;

			if (requestData.ContainsKey("userID")) 	  userID = (string)requestData["userID"];
			if (requestData.ContainsKey("sessionID")) webSessionID = (string)requestData["sessionID"];
			if (requestData.ContainsKey("startTime")) startTime = (int)requestData["startTime"];
			if (requestData.ContainsKey("endTime"))   endTime = (int)requestData["endTime"];
			if (requestData.ContainsKey("lastIndex")) lastIndex = (int)requestData["lastIndex"];

			if (m_webSessionDic.ContainsKey(userID))
			{
				if (m_webSessionDic[userID]==webSessionID)
				{
					try
					{
						int total = m_moneyDBService.getTransactionNum(userID, startTime, endTime);
						TransactionData tran = null;
						m_log.InfoFormat("[MONEY RPC]: handleWebGetTransaction: Getting transation[{0}] for user {1}", lastIndex + 1, userID);
						if (total > lastIndex + 2)
						{
							responseData["isEnd"] = false;
						}
						else
						{
							responseData["isEnd"] = true;
						}

						tran = m_moneyDBService.FetchTransaction(userID, startTime, endTime, lastIndex);
						if (tran!=null)
						{
							UserInfo senderInfo = m_moneyDBService.FetchUserInfo(tran.Sender);
							UserInfo receiverInfo = m_moneyDBService.FetchUserInfo(tran.Receiver);
							if (senderInfo!=null && receiverInfo!=null)
							{
								responseData["senderName"] = senderInfo.Avatar;
								responseData["receiverName"] = receiverInfo.Avatar;
							}
							else
							{
								responseData["senderName"] = "unknown user";
								responseData["receiverName"] = "unknown user";
							}
							responseData["success"] = true;
							responseData["transactionIndex"] = lastIndex + 1;
							responseData["transactionUUID"] = tran.TransUUID.ToString();
							responseData["senderID"] = tran.Sender;
							responseData["receiverID"] = tran.Receiver;
							responseData["amount"] = tran.Amount;
							responseData["type"] = tran.Type;
							responseData["time"] = tran.Time;
							responseData["status"] = tran.Status;
							responseData["description"] = tran.Description;
						}
						else
						{
							responseData["success"] = false;
							responseData["errorMessage"] = string.Format("Unable to fetch transaction data with the index {0}", lastIndex + 1);
						}
						return response;
					}
					catch (Exception e)
					{
						m_log.ErrorFormat("[MONEY RPC]: handleWebGetTransaction: Can't get transaction for user {0}, Exception {1}", userID, e.ToString());
						responseData["success"] = false;
						responseData["errorMessage"] = "Exception occurred when getting transaction";
						return response;
					}
				}
			}

			m_log.Error("[MONEY RPC]: handleWebGetTransaction: Session authentication failed when getting transaction for user " + userID);

			responseData["success"] = false;
			responseData["errorMessage"] = "Session check failure, please re-login";
			return response;

		}



		/// <summary>
		/// Get total number of transactions for web pages.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public XmlRpcResponse handleWebGetTransactionNum(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY RPC]: handleWebGetTransactionNum:");

			GetSSLCommonName(request);

			Hashtable requestData = (Hashtable)request.Params[0];
			XmlRpcResponse response = new XmlRpcResponse();
			Hashtable responseData = new Hashtable();
			string userID = string.Empty;
			string webSessionID = string.Empty;
			int startTime = 0;
			int endTime = 0;

			response.Value = responseData;

			if (requestData.ContainsKey("userID")) 	  userID = (string)requestData["userID"];
			if (requestData.ContainsKey("sessionID")) webSessionID = (string)requestData["sessionID"];
			if (requestData.ContainsKey("startTime")) startTime = (int)requestData["startTime"];
			if (requestData.ContainsKey("endTime"))   endTime = (int)requestData["endTime"];

			if (m_webSessionDic.ContainsKey(userID))
			{
				if (m_webSessionDic[userID]==webSessionID)
				{
					int it = m_moneyDBService.getTransactionNum(userID, startTime, endTime);
					if (it>=0)
					{
						m_log.InfoFormat("[MONEY RPC]: handleWebGetTransactionNum: Get {0} transactions for user {1}", it, userID);
						responseData["success"] = true;
						responseData["number"] = it;
					}
					else
					{
						responseData["success"] = false;
					}
					return response;
				}
			}

			m_log.Error("[MONEY RPC]: handleWebGetTransactionNum: Session authentication failed when getting transaction number for user " + userID);
			responseData["success"] = false;
			responseData["errorMessage"] = "Session check failure, please re-login";
			return response;
		}

	}
}

