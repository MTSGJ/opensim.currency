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
using System.Data;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
//using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using log4net;
using MySql.Data.MySqlClient;
using OpenMetaverse;

namespace OpenSim.Data.MySQL.MySQLMoneyDataWrapper
{
	public class MySQLMoneyManager:IMoneyManager
	{
		private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		// by Fumi.Iseki
		private string Table_of_Balance	 	= "balances";
		private string Table_of_UserInfo	= "userinfo";
		private string Table_of_Transaction = "transactions";
		//private string Table_of_UserInfo	= "currency_users";	// for Aurora-Sim

		private string connectString;

		private int m_defaultMoney = 0;

		private MySqlConnection dbcon;
  


		public MySQLMoneyManager(string hostname,string database,string username ,string password,string cpooling, string port)
		{
			string s = "Server=" + hostname + ";Port=" + port + ";Database=" + database + ";User ID=" +
				username + ";Password=" + password + ";Pooling=" + cpooling + ";";
			Initialise(s);
		}



		public MySQLMoneyManager(string connect)
		{
			Initialise(connect);
		}



		private void Initialise(string connect)
		{
			try
			{
				connectString = connect;
				dbcon = new MySqlConnection(connectString);
				try
				{
					dbcon.Open();
				}
				catch (Exception e)
				{
					throw new Exception("[MONEY DB]: Connection error while using connection string ["+connectString+"]", e);
				}
				//m_log.Info("[MONEY DB]: Connection established");
			}

			catch(Exception e)
			{
				throw new Exception("[MONEY DB]: Error initialising MySql Database: " + e.ToString());
			}

			try
			{
				Dictionary<string,string> tableList = new Dictionary<string,string>();
				tableList = CheckTables();

				// Balances Table
				if (!tableList.ContainsKey(Table_of_Balance))
				{
					try
					{
						CreateBalanceTable();
					}
					catch (Exception e)
					{
						throw new Exception("[MONEY DB]: Error creating balance table: " + e.ToString());
					}
				}
				else
				{
					string version = tableList[Table_of_Balance].Trim();
					int nVer = getTableVersionNum(version);
					switch (nVer)
					{
						case 1: //Rev.1
							UpdateBalanceTable1();
							break;
					}
				}

				//
				// UserInfo Table
				if (!tableList.ContainsKey(Table_of_UserInfo))
				{
					try
					{
						CreateUserTable();
					}
					catch (Exception e)
					{
						throw new Exception("[MONEY DB]: Unable to create currency userinfo table: " + e.ToString());
					}
				}
				else
				{
					string version = tableList[Table_of_UserInfo].Trim();
					int nVer = getTableVersionNum(version);
					switch (nVer)
					{
						case 1: //Rev.1
							UpdateUserInfoTable1();
							break;
					}
				}

				//
				// Transaction Table
				if (!tableList.ContainsKey(Table_of_Transaction))
				{
					try
					{
						CreateTransactionTable();
					}
					catch (Exception e)
					{
						throw new Exception("[MONEY DB]: Error creating transaction table: " + e.ToString());
					}
				}
				else // check transaction table version
				{
					string version = tableList[Table_of_Transaction].Trim();
					int nVer = getTableVersionNum(version);
					switch (nVer)
					{
						case 2: //Rev.2
							UpdateTransactionTable2();
							UpdateTransactionTable3();
							UpdateTransactionTable4();
							UpdateTransactionTable5();
							UpdateTransactionTable6();
							break;
						case 3: //Rev.3
							UpdateTransactionTable3();
							UpdateTransactionTable4();
							UpdateTransactionTable5();
							UpdateTransactionTable6();
							break;
						case 4: //Rev.4
							UpdateTransactionTable4();
							UpdateTransactionTable5();
							UpdateTransactionTable6();
							break;
						case 5: //Rev.5
							UpdateTransactionTable5();
							UpdateTransactionTable6();
							break;
						case 6: //Rev.6
							UpdateTransactionTable6();
							break;
					}
				}
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY DB]: Error checking or creating tables: " + e.ToString());
				throw new Exception("[MONEY DB]: Error checking or creating tables: " + e.ToString());
			}
		}



		private int getTableVersionNum(string version)
		{
			int nVer = 0;

			Regex _commentPattenRegex = new Regex(@"\w+\.(?<ver>\d+)");
			Match m = _commentPattenRegex.Match(version);
			if (m.Success)
			{
				string ver = m.Groups["ver"].Value;
				nVer = Convert.ToInt32(ver);
			}
			return nVer;
		}



		private void CreateBalanceTable()
		{
			string sql = string.Empty;

			sql += "CREATE TABLE `" + Table_of_Balance + "`(";
			sql += "`user` varchar(128) NOT NULL,";
			sql += "`balance` int(10) NOT NULL,";
			sql += "`status` tinyint(2) default NULL,";
			sql += "PRIMARY KEY (`user`))";
			sql += "Engine=InnoDB DEFAULT CHARSET=utf8 COMMENT='Rev.2';";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.ExecuteNonQuery();
		}



		private void CreateTransactionTable()
		{
			string sql = string.Empty;

			sql += "CREATE TABLE `" + Table_of_Transaction + "`(";
			sql += "`UUID` varchar(36) NOT NULL,";
			sql += "`sender` varchar(128) NOT NULL,";
			sql += "`receiver` varchar(128) NOT NULL,";
			sql += "`amount` int(10) NOT NULL,";
			sql += "`objectUUID` varchar(36) DEFAULT NULL,";
			sql += "`regionHandle` varchar(36) NOT NULL,";
			sql += "`type` int(10) NOT NULL,";
			sql += "`time` int(11) NOT NULL,";
			sql += "`secure` varchar(36) NOT NULL,";
			sql += "`status` tinyint(1) NOT NULL,";
			sql += "`commonName` varchar(128) default NULL,";
			sql += "`description` varchar(255) default NULL,";
			sql += "PRIMARY KEY (`UUID`))";
			sql += "Engine=InnoDB DEFAULT CHARSET=utf8 COMMENT='Rev.7';";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.ExecuteNonQuery();
		}



		private void CreateUserTable()
		{
			string sql = string.Empty;

			sql += "CREATE TABLE `" + Table_of_UserInfo + "`(";
			sql += "`user` varchar(128) NOT NULL,";
			sql += "`simip` varchar(64) NOT NULL,";
			sql += "`avatar` varchar(50) NOT NULL,";
			sql += "`pass` varchar(36) DEFAULT NULL,";
			sql += "PRIMARY KEY(`user`))";
			sql += "Engine=InnoDB DEFAULT CHARSET=utf8 COMMENT='Rev.2';";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.ExecuteNonQuery();
		}
		   
		


		///////////////////////////////////////////////////////////////////////

		//
		private void UpdateBalanceTable1()
		{
			m_log.Info("[MONEY DB]: Converting Balance Table...");
			string sql = string.Empty;

			sql = "SELECT COUNT(*) FROM " + Table_of_Balance;
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			int resultCount = int.Parse(cmd.ExecuteScalar().ToString()); 
			cmd.Dispose();

			sql = "SELECT * FROM " + Table_of_Balance;
			cmd = new MySqlCommand(sql, dbcon);
			MySqlDataReader dbReader = cmd.ExecuteReader();

			int l = 0;
			string[,] row = new string[resultCount, dbReader.FieldCount];
			while (dbReader.Read()) {
				for (int i=0; i<dbReader.FieldCount; i++) {
					row[l, i] = dbReader.GetString(i);
				}
				l++;
			}
			dbReader.Close();
			cmd.Dispose();

			bool updatedb = true;
			for (int i=0; i<resultCount; i++) {
				string uuid = Regex.Replace(row[i, 0], @"@.+$", "");
				if (uuid!=row[i, 0]) {
					int amount  = int.Parse(row[i,1]);
					int balance = getBalance(uuid);
					if (balance>=0) {
						amount += balance;
						updatedb = updateBalance(uuid, amount);
					}
					else {
						updatedb = addUser(uuid, amount, int.Parse(row[i,2]));
					}
					if (!updatedb) break;
				}
			}

			// Delete
			if (updatedb) {
				for (int i=0; i<resultCount; i++) {
					string uuid = Regex.Replace(row[i, 0], @"@.+$", "");
					if (uuid!=row[i, 0]) {
						sql = "DELETE FROM " + Table_of_Balance + " WHERE user = ?uuid";
						cmd = new MySqlCommand(sql, dbcon);
						cmd.Parameters.AddWithValue("?uuid", row[i,0]);
						cmd.ExecuteNonQuery();
						cmd.Dispose();
					}
				}

				//
				sql  = "BEGIN;";
				sql += "ALTER TABLE `" + Table_of_Balance + "`";
				sql += "COMMENT = 'Rev.2';";
				sql += "COMMIT;";
				cmd = new MySqlCommand(sql, dbcon);
				cmd.ExecuteNonQuery();
			}
		}



		private void UpdateUserInfoTable1()
		{
			m_log.Info("[MONEY DB]: Converting UserInfo Table...");
			string sql = string.Empty;

			sql = "SELECT COUNT(*) FROM " + Table_of_UserInfo;
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			int resultCount = int.Parse(cmd.ExecuteScalar().ToString()); 
			cmd.Dispose();

			sql = "SELECT * FROM " + Table_of_UserInfo;
			cmd = new MySqlCommand(sql, dbcon);
			MySqlDataReader dbReader = cmd.ExecuteReader();

			int l = 0;
			string[,] row = new string[resultCount, dbReader.FieldCount];
			while (dbReader.Read()) {
				for (int i=0; i<dbReader.FieldCount; i++) {
					row[l, i] = dbReader.GetString(i);
				}
				l++;
			}
			dbReader.Close();
			cmd.Dispose();

			bool updatedb = true;
			for (int i=0; i<resultCount; i++) {
				string uuid = Regex.Replace(row[i, 0], @"@.+$", "");
				if (uuid!=row[i, 0]) {
					UserInfo userInfo = fetchUserInfo(uuid);
					if (userInfo==null) {
						userInfo = new UserInfo();
						userInfo.UserID  = uuid;
						userInfo.SimIP   = row[i,1];
						userInfo.Avatar  = row[i,2];
						userInfo.PswHash = row[i,3];
						updatedb = addUserInfo(userInfo);
					}
				}
			}

			// Delete
			if (updatedb) {
				for (int i=0; i<resultCount; i++) {
					string uuid = Regex.Replace(row[i, 0], @"@.+$", "");
					if (uuid!=row[i, 0]) {
						sql = "DELETE FROM " + Table_of_UserInfo + " WHERE user = ?uuid";
						cmd = new MySqlCommand(sql, dbcon);
						cmd.Parameters.AddWithValue("?uuid", row[i,0]);
						cmd.ExecuteNonQuery();
						cmd.Dispose();
					}
				}

				//
				sql  = "BEGIN;";
				sql += "ALTER TABLE `" + Table_of_UserInfo + "`";
				sql += "COMMENT = 'Rev.2';";
				sql += "COMMIT;";
				cmd = new MySqlCommand(sql, dbcon);
				cmd.ExecuteNonQuery();
			}
		}



		/// <summary>
		/// update transaction table from Rev.2 to Rev.3
		/// </summary>
		private void UpdateTransactionTable2()
		{
			string sql = string.Empty;

			sql += "BEGIN;";
			sql += "ALTER TABLE `" + Table_of_Transaction + "`";
			sql += "ADD(`objectUUID` varchar(36) DEFAULT NULL),";
			sql += "COMMENT = 'Rev.3';";
			sql += "COMMIT;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.ExecuteNonQuery();
		}


		/// <summary>
		/// update transaction table from Rev.3 to Rev.4
		/// </summary>
		private void UpdateTransactionTable3()
		{
			string sql = string.Empty;

			sql += "BEGIN;";
			sql += "ALTER TABLE `" + Table_of_Transaction + "`";
			sql += "ADD(`secure` varchar(36) NOT NULL),";
			sql += "COMMENT = 'Rev.4';";
			sql += "COMMIT;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.ExecuteNonQuery();
		}


		/// <summary>
		/// update transaction table from Rev.4 to Rev.5
		/// </summary>
		private void UpdateTransactionTable4()
		{
			string sql = string.Empty;

			sql += "BEGIN;";
			sql += "ALTER TABLE `" + Table_of_Transaction + "`";
			sql += "ADD(`regionHandle` varchar(36) NOT NULL),";
			sql += "COMMENT = 'Rev.5';";
			sql += "COMMIT;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.ExecuteNonQuery();
		}


		/// <summary>
		/// update transaction table from Rev.5 to Rev.6
		/// </summary>
		private void UpdateTransactionTable5()
		{
			string sql = string.Empty;

			sql += "BEGIN;";
			sql += "ALTER TABLE `" + Table_of_Transaction + "`";
			sql += "ADD(`commonName` varchar(128) NOT NULL),";
			sql += "COMMENT = 'Rev.6';";
			sql += "COMMIT;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.ExecuteNonQuery();
		}


		/// <summary>
		/// update transaction table from Rev.6 to Rev.7
		/// </summary>
		private void UpdateTransactionTable6()
		{
			m_log.Info("[MONEY DB]: Converting Transaction Table...");
			string sql = string.Empty;

			sql = "SELECT COUNT(*) FROM " + Table_of_Transaction;
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			int resultCount = int.Parse(cmd.ExecuteScalar().ToString()); 
			cmd.Dispose();

			sql = "SELECT UUID,sender,receiver FROM " + Table_of_Transaction;
			cmd = new MySqlCommand(sql, dbcon);
			MySqlDataReader dbReader = cmd.ExecuteReader();

			int l = 0;
			string[,] row = new string[resultCount, dbReader.FieldCount];
			while (dbReader.Read()) {
				for (int i=0; i<dbReader.FieldCount; i++) {
					row[l, i] = dbReader.GetString(i);
				}
				l++;
			}
			dbReader.Close();
			cmd.Dispose();

			for (int i=0; i<resultCount; i++) {
				string sender   = Regex.Replace(row[i, 1], @"@.+$", "");
				string receiver = Regex.Replace(row[i, 2], @"@.+$", "");
				sql = "UPDATE " + Table_of_Transaction + " SET sender = ?sender , receiver = ?receiver where UUID = ?uuid;";
				cmd = new MySqlCommand(sql, dbcon);
				cmd.Parameters.AddWithValue("?uuid", row[i,0]);
				cmd.Parameters.AddWithValue("?sender", sender);
				cmd.Parameters.AddWithValue("?receiver", receiver);
				cmd.ExecuteNonQuery();
			}

			sql  = "BEGIN;";
			sql += "ALTER TABLE `" + Table_of_Transaction + "`";
			sql += "COMMENT = 'Rev.7';";
			sql += "COMMIT;";
			cmd = new MySqlCommand(sql, dbcon);
			cmd.ExecuteNonQuery();
		}




		///////////////////////////////////////////////////////////////////////

		private Dictionary<string,string> CheckTables()
		{
			Dictionary<string,string> tableDic = new Dictionary<string,string>();
			lock (dbcon)
			{
				string sql = string.Empty;

				sql += "SELECT TABLE_NAME,TABLE_COMMENT FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=?dbname";
				MySqlCommand cmd = new MySqlCommand(sql, dbcon);
				cmd.Parameters.AddWithValue("?dbname", dbcon.Database);

				using (MySqlDataReader r = cmd.ExecuteReader())
				{
					while (r.Read())
					{
						try
						{
							string tableName = (string)r["TABLE_NAME"];
							string comment = (string)r["TABLE_COMMENT"];
							tableDic.Add(tableName, comment);
						}
						catch (Exception e)
						{
							throw new Exception("[MONEY DB]: Error checking tables" + e.ToString());
						}
					}
					r.Close();
				}
				return tableDic;
			}
		}


		/// <summary>
		/// Reconnect to the database
		/// </summary>
		public void Reconnect()
		{
			m_log.Info("[MONEY DB]: Reconnecting database");
			lock (dbcon)
			{
				try
				{
					dbcon.Close();
					dbcon = new MySqlConnection(connectString);
					dbcon.Open();
					m_log.Info("[MONEY DB]: Reconnected database");
				}
				catch (Exception e)
				{
					m_log.Error("[MONEY DB]: Unable to reconnect to database" + e.ToString());
				}
			}
		}



		///////////////////////////////////////////////////////////////////////

		/// <summary>
		/// Get balance from database. returns -1 if failed.
		/// </summary>
		/// <param name="userID"></param>
		/// <returns></returns>
		public int getBalance(string userID)
		{
			string sql = string.Empty;
			int retValue = -1;
			sql += "SELECT balance FROM " + Table_of_Balance + " WHERE user = ?userid";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?userid", userID);
			try
			{
				using (MySqlDataReader dbReader = cmd.ExecuteReader(CommandBehavior.SingleRow))
				{
					if (dbReader.Read())
					{
						retValue = Convert.ToInt32(dbReader["balance"]);
					}
					dbReader.Close();
					cmd.Dispose();
				}
			}
			catch (Exception e)
			{
				m_log.ErrorFormat("[MoneyDB]: MySql failed to fetch balance {0}" + Environment.NewLine + e.ToString()
															+ Environment.NewLine + "Reconnecting" + userID);
				Reconnect();
				return -2;
			}
			return retValue;
		}


		public bool updateBalance(string uuid, int amount)
		{
			bool bRet = false;
			string sql = string.Empty;

			sql += "UPDATE " + Table_of_Balance + " SET balance = ?amount where user = ?uuid;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?amount", amount);
			cmd.Parameters.AddWithValue("?uuid", uuid);

			try
			{
				if (cmd.ExecuteNonQuery() > 0) bRet = true;
				cmd.Dispose();
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY DB]: update money error " + e.ToString());
				return false;
			}

			return bRet;
		}


		public bool addUser(string userID,int balance,int status)
		{
			bool bRet = false;
			string sql = string.Empty;

			sql += "INSERT INTO " + Table_of_Balance + " (`user`,`balance`,`status`) VALUES ";
			sql += "(?userID,?balance,?status);";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);

			cmd.Parameters.AddWithValue("?userID",  userID);
			cmd.Parameters.AddWithValue("?balance", balance);
			cmd.Parameters.AddWithValue("?status",  status);
			if (cmd.ExecuteNonQuery() > 0) bRet = true;

			return bRet;
		}


		/// <summary>
		/// Add user,for internal use
		/// </summary>
		/// <param name="userID"></param>
		private void addUser(string userID)
		{
			string sql = string.Empty;

			sql += "INSERT INTO " + Table_of_Balance + " (`user`,`balance`,`status`) VALUES ";
			sql += "(?userID,?balance,?status)";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);

			cmd.Parameters.AddWithValue("?userID",  userID);
			cmd.Parameters.AddWithValue("?balance", m_defaultMoney);
			cmd.Parameters.AddWithValue("?status",  0);
			cmd.ExecuteNonQuery();
		}


		/// <summary>
		/// Here we'll make a withdraw from the sender and update transaction status
		/// </summary>
		/// <param name="fromID"></param>
		/// <param name="toID"></param>
		/// <param name="amount"></param>
		/// <returns></returns>
		public bool withdrawMoney(UUID transactionID,string senderID, int amount)
		{
			bool bRet = false;
			string sql = string.Empty;

			sql += "BEGIN;";
			sql += "UPDATE " + Table_of_Balance + " SET balance = balance - ?amount where user = ?userid;";
			sql += "UPDATE " + Table_of_Transaction + " SET status = ?status where UUID  = ?tranid;";
			sql += "COMMIT;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);

			cmd.Parameters.AddWithValue("?amount", amount);
			cmd.Parameters.AddWithValue("?userid", senderID);
			cmd.Parameters.AddWithValue("?status", (int)Status.PENDING_STATUS);	//pending
			cmd.Parameters.AddWithValue("?tranid", transactionID.ToString());

			try
			{
				if (cmd.ExecuteNonQuery() > 0) bRet = true;
				cmd.Dispose();
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY DB]: withdraw money error " + e.ToString());
				return false;
			}

			return bRet;
		}


		/// <summary>
		/// Give money to the receiver and change the transaction status to success.
		/// </summary>
		/// <param name="transactionID"></param>
		/// <param name="receiverID"></param>
		/// <param name="amount"></param>
		/// <returns></returns>
		public bool giveMoney(UUID transactionID, string receiverID, int amount)
		{
			string sql = string.Empty;
			bool bRet  = false;

			sql += "BEGIN;";
			sql += "UPDATE " + Table_of_Balance + " SET balance = balance + ?amount where user = ?userid;";
			sql += "UPDATE " + Table_of_Transaction + " SET status = ?status where UUID  = ?tranid;";
			sql += "COMMIT;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);

			cmd.Parameters.AddWithValue("?amount", amount);
			cmd.Parameters.AddWithValue("?userid", receiverID);
			cmd.Parameters.AddWithValue("?status", (int)Status.SUCCESS_STATUS);//Success
			cmd.Parameters.AddWithValue("?tranid", transactionID.ToString());

			try
			{
				if (cmd.ExecuteNonQuery() > 0) bRet = true;
				cmd.Dispose();
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY DB]: give money error " + e.ToString());
				return false;
			}

			return bRet;
		}



		///////////////////////////////////////////////////////////////////////
		//
		// transaction
		//
		public bool addTransaction(TransactionData transaction)
		{
			bool bRet = false;
			string sql = string.Empty;

			sql += "INSERT INTO " + Table_of_Transaction;
			sql += " (`UUID`,`sender`,`receiver`,`amount`,`objectUUID`,`regionHandle`,`type`,`time`,`secure`,`status`,`commonName`,`description`) VALUES";
			sql += " (?transID,?sender,?receiver,?amount,?objID,?regionHandle,?type,?time,?secure,?status,?cname,?desc)";

			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?transID", transaction.TransUUID.ToString());
			cmd.Parameters.AddWithValue("?sender", transaction.Sender);
			cmd.Parameters.AddWithValue("?receiver", transaction.Receiver);
			cmd.Parameters.AddWithValue("?amount", transaction.Amount);
			cmd.Parameters.AddWithValue("?objID", transaction.ObjectUUID);
			cmd.Parameters.AddWithValue("?regionHandle", transaction.RegionHandle);
			cmd.Parameters.AddWithValue("?type", transaction.Type);
			cmd.Parameters.AddWithValue("?time", transaction.Time);
			cmd.Parameters.AddWithValue("?secure", transaction.SecureCode);
			cmd.Parameters.AddWithValue("?status", transaction.Status);
			cmd.Parameters.AddWithValue("?cname", transaction.CommonName);
			cmd.Parameters.AddWithValue("?desc", transaction.Description);

			try
			{
				if (cmd.ExecuteNonQuery() > 0) bRet = true;
				cmd.Dispose();
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY DB]: Error adding transation to DB: " + e.ToString());
				return false;
			}
			return bRet;
		}


		public bool updateTransactionStatus(UUID transactionID, int status, string description)
		{
			bool bRet = false;
			string sql = string.Empty;

			sql += "UPDATE " + Table_of_Transaction + " SET status = ?status,description = ?desc where UUID  = ?tranid;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?status", status);
			cmd.Parameters.AddWithValue("?desc", description);
			cmd.Parameters.AddWithValue("?tranid", transactionID);

			try
			{
				if (cmd.ExecuteNonQuery() > 0) bRet = true;
				cmd.Dispose();
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY DB]: Error updating transation in DB: " + e.ToString());
				return false;
			}
			return bRet;
		}


		public bool SetTransExpired(int deadTime)
		{
			bool bRet = false;
			string sql = string.Empty;

			sql += "UPDATE " + Table_of_Transaction;
			sql += " SET status = ?failedstatus,description = ?desc where time <= ?deadTime and status = ?pendingstatus;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?failedstatus", (int)Status.FAILED_STATUS);
			cmd.Parameters.AddWithValue("?desc", "expired");
			cmd.Parameters.AddWithValue("?deadTime", deadTime);
			cmd.Parameters.AddWithValue("?pendingstatus", (int)Status.PENDING_STATUS);

			try
			{
				if (cmd.ExecuteNonQuery() > 0) bRet = true;
				cmd.Dispose();
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY DB]: Error updating transation in DB: " + e.ToString());
				return false;
			}
			return bRet;
		}


		/// <summary>
		/// Validate if the transacion is legal
		/// </summary>
		/// <param name="userID"></param>
		/// <param name="transactionID"></param>
		/// <returns></returns>
		public bool ValidateTransfer(string secureCode, UUID transactionID)
		{
			bool bRet = false;
			string secure = string.Empty;
			string sql = string.Empty;

			sql += "SELECT secure from " + Table_of_Transaction + " where UUID = ?transID;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?transID", transactionID.ToString());
			using (MySqlDataReader r = cmd.ExecuteReader())
			{
				if(r.Read())
				{
					try
					{
						secure = (string)r["secure"];
					}
					catch (Exception e)
					{
						m_log.Error("[MONEY DB]: get transaction from DB failed: " + e.ToString());
						return false;
					}
					if (secureCode == secure) bRet = true;
					else bRet = false;
				}
				r.Close();
			}
			return bRet;
		}


		public TransactionData FetchTransaction(UUID transactionID)
		{
			TransactionData transactionData = new TransactionData();
			transactionData.TransUUID = transactionID;
			string sql = string.Empty;

			sql += "SELECT * from " + Table_of_Transaction + " where UUID = ?transID;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?transID", transactionID.ToString());

			using (MySqlDataReader r = cmd.ExecuteReader())
			{
				if (r.Read())
				{
					try
					{
						transactionData.Sender = (string)r["sender"];
						transactionData.Receiver = (string)r["receiver"];
						transactionData.Amount = Convert.ToInt32(r["amount"]);
						transactionData.ObjectUUID = (string)r["objectUUID"];
						transactionData.RegionHandle = (string)r["regionHandle"];
						transactionData.Type = Convert.ToInt32(r["type"]);
						transactionData.Time = Convert.ToInt32(r["time"]);
						transactionData.Status = Convert.ToInt32(r["status"]);
						transactionData.CommonName  = (string)r["commonName"];
						transactionData.Description = (string)r["description"];
					}
					catch (Exception e)
					{
						m_log.Error("[MONEY DB]: Fetching transaction failed: " + e.ToString());
						return null;
					}

				}
				r.Close();
			}

			return transactionData;
		}



		public TransactionData[] FetchTransaction(string userID, int startTime, int endTime, uint index, uint retNum)
		{
			List<TransactionData> rows = new List<TransactionData>();
			string sql = string.Empty;

			sql += "SELECT * from " + Table_of_Transaction + " where time>=?start AND time<=?end ";
			sql += "AND (sender=?user or receiver=?user) order by time asc limit ?index,?num;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?start", startTime);
			cmd.Parameters.AddWithValue("?end", endTime);
			cmd.Parameters.AddWithValue("?user", userID);
			cmd.Parameters.AddWithValue("?index", index);
			cmd.Parameters.AddWithValue("?num", retNum);

			using (MySqlDataReader r = cmd.ExecuteReader())
			{
				for (int i = 0; i < retNum; i++)
				{
					if (r.Read())
					{
						try
						{
							TransactionData transactionData = new TransactionData();
							string uuid = (string)r["UUID"];
							UUID transUUID;
							UUID.TryParse(uuid,out transUUID);

							transactionData.TransUUID = transUUID;
							transactionData.Sender = (string)r["sender"];
							transactionData.Receiver = (string)r["receiver"];
							transactionData.Amount = Convert.ToInt32(r["amount"]);
							if (r["objectUUID"] is System.DBNull)
							{
								transactionData.ObjectUUID = "null";
							}
							else
							{
								transactionData.ObjectUUID = (string)r["objectUUID"];
							}
							transactionData.Type = Convert.ToInt32(r["type"]);
							transactionData.Time = Convert.ToInt32(r["time"]);
							transactionData.Status = Convert.ToInt32(r["status"]);
							transactionData.CommonName  = (string)r["commonName"];
							transactionData.Description = (string)r["description"];
							rows.Add(transactionData);
						}

						catch (Exception e)
						{
							m_log.Error("[MONEY DB]: Fetching transaction failed: " + e.ToString());
							return null;
						}

					}
				}
				r.Close();
			}

			cmd.Dispose();
			return rows.ToArray();
		}


		public int getTransactionNum(string userID, int startTime, int endTime)
		{
			int iRet = -1;
			string sql = string.Empty;

			sql += "SELECT COUNT(*) AS number FROM " + Table_of_Transaction + " WHERE time>=?start AND time<=?end ";
			sql += "AND (sender=?user OR receiver=?user);";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?start", startTime);
			cmd.Parameters.AddWithValue("?end", endTime);
			cmd.Parameters.AddWithValue("?user", userID);

			using (MySqlDataReader r = cmd.ExecuteReader())
			{
				if(r.Read())
				{
					try
					{
						iRet = Convert.ToInt32(r["number"]);
					}
					catch (Exception e)
					{
						m_log.Error("[MONEY DB]: Unable to get transaction info: " + e.ToString());
						return -1;
					}
				}
			}
			cmd.Dispose();
			return iRet;
		}



		///////////////////////////////////////////////////////////////////////
		//
		// userinfo
		//
		public bool addUserInfo(UserInfo userInfo)
		{
			bool bRet = false;
			string sql = string.Empty;
		   
			if (userInfo.Avatar==null) return false;

			sql += "INSERT INTO " + Table_of_UserInfo +"(`user`,`simip`,`avatar`,`pass`) VALUES";
			sql += "(?user,?simip,?avatar,?password);";

			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?user", userInfo.UserID);
			cmd.Parameters.AddWithValue("?simip", userInfo.SimIP);
			cmd.Parameters.AddWithValue("?avatar", userInfo.Avatar);
			cmd.Parameters.AddWithValue("?password", userInfo.PswHash);

			try
			{
				if (cmd.ExecuteNonQuery() > 0) bRet = true;
				cmd.Dispose();
			}
			catch (Exception e)
			{
				m_log.Error("[MONEY DB]: Unable to add user information to database: " + e.ToString());
				bRet = false;
			}

			return bRet;
		}


		public UserInfo fetchUserInfo(string userID)
		{
			UserInfo userInfo = new UserInfo();
			userInfo.UserID = userID;
			string sql = string.Empty;

			sql += "SELECT * from " + Table_of_UserInfo + " WHERE user = ?userID;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?userID", userID);

			using (MySqlDataReader r = cmd.ExecuteReader())
			{
				if (r.Read())
				{
					try
					{
						userInfo.SimIP = (string)r["simip"];
						userInfo.Avatar = (string)r["avatar"];
						userInfo.PswHash = (string)r["pass"];
					}
					catch (Exception e)
					{
						m_log.Error("[MONEY DB]: Fetching UserInfo failed: " + e.ToString());
						return null;
					}
				}
				else
				{
					return null;
				}
				r.Close();
			}

			return userInfo;
		}


		public bool updateUserInfo(UserInfo user)
		{
			bool bRet = false;
			string sql = string.Empty;

			sql += "UPDATE " + Table_of_UserInfo + " SET simip=?simip,avatar=?avatar,pass=?pass WHERE user=?user;";
			MySqlCommand cmd = new MySqlCommand(sql, dbcon);
			cmd.Parameters.AddWithValue("?simip", user.SimIP);
			cmd.Parameters.AddWithValue("?avatar", user.Avatar);
			cmd.Parameters.AddWithValue("?pass", user.PswHash);
			cmd.Parameters.AddWithValue("?user", user.UserID);

			if (cmd.ExecuteNonQuery() > 0) bRet = true;
			else bRet = false;
			cmd.Dispose();

			return bRet;
		}


	}
}
