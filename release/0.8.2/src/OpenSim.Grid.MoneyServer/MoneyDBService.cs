/*
 * Copyright (c) Contributors, http://opensimulator.org/, http://www.nsl.tuis.ac.jp/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
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
//using System.Linq;
using System.Text;
using OpenSim.Data.MySQL.MySQLMoneyDataWrapper;
using log4net;
using System.Reflection;
using OpenMetaverse;


namespace OpenSim.Grid.MoneyServer
{
    class MoneyDBService: IMoneyDBService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private string m_connect;
        private MySQLMoneyManager m_moneyManager;

        // DB manager pool
        protected Dictionary<int, MySQLSuperManager> m_dbconnections = new Dictionary<int, MySQLSuperManager>();
        private int m_maxConnections;
        public int m_lastConnect = 0;


        public MoneyDBService(string connect)
        {
            m_connect = connect;
            Initialise(m_connect,10);
        }


        public MoneyDBService()
        {
        }


        public void Initialise(string connectionString,int maxDBConnections)
        {
            m_maxConnections = maxDBConnections;
            if (connectionString != string.Empty)
            {
                m_moneyManager = new MySQLMoneyManager(connectionString);
                //m_log.Info("Creating " + m_maxConnections + " DB connections...");
                for (int i = 0; i < m_maxConnections; i++)
                {
                    //m_log.Info("Connecting to DB... [" + i + "]");
                    MySQLSuperManager msm = new MySQLSuperManager();
                    msm.Manager = new MySQLMoneyManager(connectionString);
                    m_dbconnections.Add(i, msm);
                }
            }
            else 
            {
                m_log.Error("[MONEY DB]: Connection string is null,initialise database failed");
                throw new Exception("Failed to initialise MySql database");
            }
        }


        private MySQLSuperManager GetLockedConnection()
        {
            int lockedCons = 0;
            while (true)
            {
                m_lastConnect++;

                // Overflow protection
                if (m_lastConnect == int.MaxValue)
                    m_lastConnect = 0;

                MySQLSuperManager x = m_dbconnections[m_lastConnect % m_maxConnections];
                if (!x.Locked)
                {
                    x.GetLock();
                    return x;
                }

                lockedCons++;
                if (lockedCons > m_maxConnections)
                {
                    lockedCons = 0;
                    System.Threading.Thread.Sleep(1000); // Wait some time before searching them again.
                    m_log.Debug(
                        "WARNING: All threads are in use. Probable cause: Something didnt release a mutex properly, or high volume of requests inbound.");
                }
            }
        }


        public int getBalance(string userID)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.getBalance(userID);
            }
            catch(Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return 0;
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool withdrawMoney(UUID transactionID, string senderID, int amount)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.withdrawMoney(transactionID, senderID, amount);
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool giveMoney(UUID transactionID, string receiverID, int amount)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.giveMoney(transactionID, receiverID, amount);
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool addTransaction(TransactionData transaction)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.addTransaction(transaction);
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool addUser(string userID, int balance, int status)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.addUser(userID, balance, status);
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool updateTransactionStatus(UUID transactionID, int status, string description)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.updateTransactionStatus(transactionID, status, description);
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool SetTransExpired(int deadTime)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.SetTransExpired(deadTime);
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool ValidateTransfer(string secureCode, UUID transactionID)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.ValidateTransfer(secureCode, transactionID);
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }


        public TransactionData FetchTransaction(UUID transactionID)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.FetchTransaction(transactionID);
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return null;
            }
            finally
            {
                dbm.Release();
            }
        }


        public TransactionData FetchTransaction(string userID, int startTime, int endTime, int lastIndex)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            TransactionData[] arrTransaction;
            try
            {
                if (lastIndex < 0)
                {
                    arrTransaction = dbm.Manager.FetchTransaction(userID, startTime, endTime, 0, 1);
                    if (arrTransaction.Length > 0)
                    {
                        return arrTransaction[0];
                    }
                    else
                        return null;
                }
                else
                {
                    uint index = Convert.ToUInt32(lastIndex);
                    arrTransaction = dbm.Manager.FetchTransaction(userID, startTime, endTime, index + 1, 1);
                    if (arrTransaction.Length > 0)
                    {
                        return arrTransaction[0];
                    }
                    else
                        return null;
                }
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return null;
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool DoTransfer(UUID transactionUUID)
        {
            TransactionData transaction = new TransactionData();
            transaction = FetchTransaction(transactionUUID);
            if (transaction != null && transaction.Status == (int)Status.PENDING_STATUS)
            {
                int balance = getBalance(transaction.Sender);
                //check the amount
                if (transaction.Amount >= 0 && balance >= transaction.Amount)
                {
                    if (withdrawMoney(transactionUUID, transaction.Sender, transaction.Amount))
                    {
                        //If receiver not found, add it to DB.
                        if (getBalance(transaction.Receiver) == -1)
                        {
                            addUser(transaction.Receiver, 0, (int)Status.SUCCESS_STATUS);
                        }
                        if (giveMoney(transactionUUID, transaction.Receiver, transaction.Amount))
                            return true;
                        else // give money to receiver failed.
                        {
                            m_log.ErrorFormat("[MONEY DB]: Give money to receiver {0} failed", transaction.Receiver);
                            //Return money to sender
                            if (giveMoney(transactionUUID, transaction.Sender, transaction.Amount))
                            {
                                m_log.ErrorFormat("[MONEY DB]: give money to receiver {0} failed but return it to sender {1} successfully",
                                    transaction.Receiver,
                                    transaction.Sender);
                                updateTransactionStatus(transactionUUID,
                                    (int)Status.FAILED_STATUS,
                                    "give money to receiver failed but return it to sender successfully");
                            }
                            else
                            {
                                m_log.ErrorFormat("[MONEY DB]: FATAL ERROR: Money withdrawn from sender: {0}, but failed to be given to receiver {1}",
                                    transaction.Sender, transaction.Receiver);
                            }
                        }
                    }
                    else // withdraw money failed
                    {
                        m_log.ErrorFormat("[MONEY DB]: Withdraw money from sender {0} failed", transaction.Sender);
                    }
                }
                else // not enough balance to finish the transaction
                {
                    m_log.ErrorFormat("[MONEY DB]: Not enough balance for user: {0} to apply the transaction.", transaction.Sender);
                }
            }
            else // Can not fetch the transaction or it has expired
            {
                m_log.ErrorFormat("[MONEY DB]: The transaction:{0} has expired", transactionUUID.ToString());
            }
            return false;
        }


		// by Fumi.Iseki
        public bool DoAddMoney(UUID transactionUUID)
        {
            TransactionData transaction = new TransactionData();
            transaction = FetchTransaction(transactionUUID);
			
            if (transaction != null && transaction.Status == (int)Status.PENDING_STATUS)
            {
                //If receiver not found, add it to DB.
                if (getBalance(transaction.Receiver)==-1)
                {
                    addUser(transaction.Receiver, 0, (int)Status.SUCCESS_STATUS);
                }

                if (giveMoney(transactionUUID, transaction.Receiver, transaction.Amount)) return true;
                else // give money to receiver failed.
                {
                    m_log.ErrorFormat("[MONEY DB]: Add money to receiver {0} failed", transaction.Receiver);
                    updateTransactionStatus(transactionUUID, (int)Status.FAILED_STATUS, "add money to receiver failed");
				}
            }
            else // Can not fetch the transaction or it has expired
            {
                m_log.ErrorFormat("[MONEY DB]: The transaction:{0} has expired", transactionUUID.ToString());
            }
            return false;
        }


        public bool TryAddUserInfo(UserInfo user)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                if (dbm.Manager.fetchUserInfo(user.UserID) != null)
                {
                    m_log.InfoFormat("[MONEY DB]: Found user \"{0}\", now update information", user.Avatar);
                    if (m_moneyManager.updateUserInfo(user))
                        return true;
                }
                else if (dbm.Manager.addUserInfo(user))
                {
                    m_log.InfoFormat("[MONEY DB]: Unable to find user \"{0}\", add it to DB successfully", user.Avatar);
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                // Fumi.Iseki
                m_moneyManager.Reconnect();
                m_log.Error(e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }


        public UserInfo FetchUserInfo(string userID)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.fetchUserInfo(userID);
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return null;
            }
            finally
            {
                dbm.Release();
            }
        }


        public int getTransactionNum(string userID, int startTime, int endTime)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.getTransactionNum(userID,startTime,endTime);
            }
            catch (Exception e)
            {
                dbm.Manager.Reconnect();
                m_log.Error(e.ToString());
                return -1;
            }
            finally
            {
                dbm.Release();
            }
        }
    }
}
