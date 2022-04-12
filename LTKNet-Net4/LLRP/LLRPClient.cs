
    /*
    ***************************************************************************
    *  Copyright 2008 Impinj, Inc.
    *
    *  Licensed under the Apache License, Version 2.0 (the "License");
    *  you may not use this file except in compliance with the License.
    *  You may obtain a copy of the License at
    *
    *      http://www.apache.org/licenses/LICENSE-2.0
    *
    *  Unless required by applicable law or agreed to in writing, software
    *  distributed under the License is distributed on an "AS IS" BASIS,
    *  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    *  See the License for the specific language governing permissions and
    *  limitations under the License.
    *
    ***************************************************************************
    */

    /*
    ***************************************************************************
    *
    *  This code is generated by Impinj LLRP .Net generator. Modification is
    *  not recommended.
    *
    ***************************************************************************
    */

    /*
    ***************************************************************************
    * File Name:       LLRPClient.cs
    *
    * Version:         1.0
    * Author:          Impinj
    * Organization:    Impinj
    * Date:            Jan. 18, 2008
    *
    * Description:     This file contains implementation of LLRP client. LLRP
    *                 client is used to build LLRP based application
    ***************************************************************************
    */


    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Runtime.Remoting;
    using System.Collections;
    using System.Xml;
    using System.Xml.Serialization;
    using System.Data;

    using Org.LLRP.LTK.LLRPV1.DataType;

    namespace Org.LLRP.LTK.LLRPV1
    {

    //delegates for sending asyn. messages
    public delegate void delegateReaderEventNotification(MSG_READER_EVENT_NOTIFICATION msg);
    public delegate void delegateRoAccessReport(MSG_RO_ACCESS_REPORT msg);
    public delegate void delegateKeepAlive(MSG_KEEPALIVE msg);

    //Delegates for sending encapsulated asyn. messages
    public delegate void delegateEncapReaderEventNotification(ENCAPED_READER_EVENT_NOTIFICATION msg);
    public delegate void delegateEncapRoAccessReport(ENCAPED_RO_ACCESS_REPORT msg);
    public delegate void delegateEncapKeepAlive(ENCAPED_KEEP_ALIVE msg);

    [Serializable]
    /// <summary>
  /// Device proxy for host application to connect with LLRP reader
  /// </summary>
    public class LLRPClient : IDisposable
    {
    #region Network Parameters
    private CommunicationInterface cI;
    private int LLRP_TCP_PORT = 5084;
    private int MSG_TIME_OUT = 10000;
    #endregion

    #region Private Thread Objects
    private Thread notificationThread;
    private BlockingQueue notificationQueue;
    #endregion

    #region Private Members
    private ManualResetEvent conn_evt;
    private ENUM_ConnectionAttemptStatusType conn_status_type;
    private string reader_name;
    private bool connected = false;
    #endregion


    public event delegateReaderEventNotification OnReaderEventNotification;
    public event delegateRoAccessReport OnRoAccessReportReceived;
    public event delegateKeepAlive OnKeepAlive;

    public event delegateEncapReaderEventNotification OnEncapedReaderEventNotification;
    public event delegateEncapRoAccessReport OnEncapedRoAccessReportReceived;
    public event delegateEncapKeepAlive OnEncapedKeepAlive;

    protected void TriggerReaderEventNotification(MSG_READER_EVENT_NOTIFICATION msg)
    {
        try {
            if (OnReaderEventNotification != null) OnReaderEventNotification(msg);
            if (OnEncapedReaderEventNotification != null)
            {
            ENCAPED_READER_EVENT_NOTIFICATION ntf = new ENCAPED_READER_EVENT_NOTIFICATION();
            ntf.reader = reader_name;
            ntf.ntf = msg;
            }
        }
        catch { }
    }

    protected void TriggerRoAccessReport(MSG_RO_ACCESS_REPORT msg)
    {
        try
        {
            if (OnRoAccessReportReceived != null) OnRoAccessReportReceived(msg);
            if (OnEncapedRoAccessReportReceived != null)
            {
                ENCAPED_RO_ACCESS_REPORT report = new ENCAPED_RO_ACCESS_REPORT();
                report.reader = reader_name;
                report.report = msg;

                OnEncapedRoAccessReportReceived(report);
            }
        }
        catch { }
    }

    protected void TriggerKeepAlive(MSG_KEEPALIVE msg)
    {
        try 
        {
            if (OnKeepAlive != null) OnKeepAlive(msg);
            if (OnEncapedKeepAlive != null)
            {
                ENCAPED_KEEP_ALIVE keepalive = new ENCAPED_KEEP_ALIVE();
                keepalive.reader = reader_name;
                keepalive.keep_alive = msg;

                OnEncapedKeepAlive(keepalive);
            }
        }
        catch { }
    }

    #region Properties
    /// <summary>
  /// Reader name.
  /// </summary>
    public string ReaderName
    {
        get{return reader_name;}
    }
    
    public bool IsConnected
    {
        get{return connected;}
    }



    #endregion

    #region Assistance Functions

    /// <summary>
    /// Set LLRP message time out
    /// </summary>
    public void SetMessageTimeOut(int time_out)
    {
        this.MSG_TIME_OUT = time_out;
    }

    /// <summary>
  /// Get LLRP message time out.
  /// </summary>
    public int GetMessageTimeOut()
    {
        return MSG_TIME_OUT;
    }

    #endregion

    #region Methods

    /// <summary>
    /// LLRPClient constructor using default TCP port.
    /// </summary>
    public LLRPClient() : this(5084)
    {
    }

    /// <summary>
    /// LLRPClient constructor with specified TCP port.
    /// </summary>
    /// <param name="port">TCP communication port. int</param>
    public LLRPClient(int port)
    {
        this.LLRP_TCP_PORT = port;
        cI = new TCPIPClient();
        notificationQueue = new BlockingQueue();
        notificationThread = new Thread(this.ProcessNotificationQueue);
        notificationThread.Name = "LLRPClient.notificationThread";
        notificationThread.IsBackground = true;
        notificationThread.Start();
    }

    /// <summary>
    /// This thread runs as the sole LTKNET thread.  It's job is to
    /// dispatch asyncrhonous notifications to the client in a way
    /// that doesn't cause mis-ordering (beginInvoke) of events
    /// but also allows the client thread to call syncrhonous
    /// LTKNET APIs from their event handlers */
    /// </summary>
    private void ProcessNotificationQueue()
    {
        if (notificationQueue != null)
        {
            try
            {
                while (true)
                {
                    Message msg = (Message) notificationQueue.Dequeue();
                    switch ((ENUM_LLRP_MSG_TYPE) msg.MSG_TYPE)
                    {
                        case ENUM_LLRP_MSG_TYPE.RO_ACCESS_REPORT:
                        TriggerRoAccessReport((MSG_RO_ACCESS_REPORT) msg);
                        break;
                        case ENUM_LLRP_MSG_TYPE.KEEPALIVE:
                        TriggerKeepAlive((MSG_KEEPALIVE) msg);
                        break;
                        case ENUM_LLRP_MSG_TYPE.READER_EVENT_NOTIFICATION:
                        TriggerReaderEventNotification((MSG_READER_EVENT_NOTIFICATION)msg);
                        break;
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                if ("Queue Closed" == ex.Message) { } else throw;
            }
        }
    }

    /// <summary>
    /// Open connection to LLRP reader.
    /// </summary>
    /// <param name="llrp_reader_name">Reader name, could either be IP address or DNS name. string</param>
    /// <param name="timeout">Time out in millisecond for trying to connect to reader. int</param>
    /// <param name="status">Connection attempt status.</param>
    /// <returns>True if the reader is opened without error.</returns>
    /// <exception cref="LLRPNetworkException">Throw LLRPNetworkException when the network is unreable</exception>
    public bool Open(string llrp_reader_name, int timeout, out ENUM_ConnectionAttemptStatusType status)
    {
        reader_name = llrp_reader_name;

        status = (ENUM_ConnectionAttemptStatusType)(-1);
        cI.OnFrameReceived += new delegateMessageReceived(ProcessFrame);

        try{ cI.Open(llrp_reader_name, LLRP_TCP_PORT, timeout);}
        catch (Exception e)
        {
            cI.OnFrameReceived -= new delegateMessageReceived(ProcessFrame);
            throw e;
        }

        conn_evt = new ManualResetEvent(false);
        if (conn_evt.WaitOne(timeout, false))
        {
            status = conn_status_type;
            if(status== ENUM_ConnectionAttemptStatusType.Success)
            {
                connected = true;
                return connected;
            }
        }

        reader_name = llrp_reader_name;

        try
        {
            cI.Close();
            cI.OnFrameReceived -= new delegateMessageReceived(ProcessFrame);
        }
        catch
        {
        }

        connected  = false;
        return connected;
    }

    /// <summary>
    /// Close LLRP connection.
    /// </summary>
    /// <returns>True if the connection is closed successfully.</returns>
    public bool Close()
    {
        try
        {
            MSG_CLOSE_CONNECTION msg = new MSG_CLOSE_CONNECTION();
            MSG_ERROR_MESSAGE msg_err;
            MSG_CLOSE_CONNECTION_RESPONSE rsp = this.CLOSE_CONNECTION(msg, out msg_err, MSG_TIME_OUT);
            bool ret = true;

            if (rsp == null || rsp.LLRPStatus.StatusCode != ENUM_StatusCode.M_Success) ret = false;

            try
            {
                cI.Close();
            }
            catch { }
	    finally
	    {
                cI.OnFrameReceived -= new delegateMessageReceived(ProcessFrame);
                connected = false;
	    }
            
	    try
	    {
                notificationQueue.Close();
	    }
            catch { }

            return ret;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Implement IDisposible interface.
    /// </summary>
    public void Dispose()
    {
    this.Close();
    }

    private void ProcessFrame(Int16 ver, Int16 msg_type, Int32 msg_id, byte[] data)
    {
        BitArray bArr;
        int cursor = 0;
        int length;

        switch((ENUM_LLRP_MSG_TYPE)msg_type)
        {
    
          case ENUM_LLRP_MSG_TYPE.RO_ACCESS_REPORT:
              try
              {
                  bArr = Util.ConvertByteArrayToBitArray(data);
                  length = bArr.Count;
                  MSG_RO_ACCESS_REPORT rpt = MSG_RO_ACCESS_REPORT.FromBitArray(ref bArr, ref cursor, length);
                  notificationQueue.Enqueue(rpt);
              }
              catch
              {
              }
          break;
        
          case ENUM_LLRP_MSG_TYPE.KEEPALIVE:
          try
          {
              bArr = Util.ConvertByteArrayToBitArray(data);
              length = bArr.Count;
              MSG_KEEPALIVE msg = MSG_KEEPALIVE.FromBitArray(ref bArr, ref cursor, length);
              notificationQueue.Enqueue(msg);
          }
          catch
          {
          }
          break;
        
          case ENUM_LLRP_MSG_TYPE.READER_EVENT_NOTIFICATION:
              try
              {
                  bArr = Util.ConvertByteArrayToBitArray(data);
                  length = bArr.Count;
                  MSG_READER_EVENT_NOTIFICATION ntf = MSG_READER_EVENT_NOTIFICATION.FromBitArray(ref bArr, ref cursor, length);
                  if (conn_evt != null && ntf.ReaderEventNotificationData.ConnectionAttemptEvent != null)
                  {
                      conn_status_type = ntf.ReaderEventNotificationData.ConnectionAttemptEvent.Status;
                      conn_evt.Set();
                  }
                  else
                  {
                      notificationQueue.Enqueue(ntf);
                  }
              }
              catch
              {
              }
              break;
        
    default:
    break;
    }
    }

    #endregion

    
        /// <summary>
        /// Send Customized Message to Reader
        /// </summary>
        /// <param name="msg">Message to be sent to reader.</param>
        /// <param name="msg_err">Error message. output</param>
        /// <param name="time_out">Timeout in millisecond.</param>
        /// <returns>Custom Message</returns>
        public MSG_CUSTOM_MESSAGE  CUSTOM_MESSAGE(MSG_CUSTOM_MESSAGE msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.CUSTOM_MESSAGE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_CUSTOM_MESSAGE) rsp;
        }
      

        /// <summary>
        /// GET_READER_CAPABILITIES message call.
        /// </summary>
        /// <param name="msg">MSG_GET_READER_CAPABILITIES to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_GET_READER_CAPABILITIES_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_GET_READER_CAPABILITIES_RESPONSE GET_READER_CAPABILITIES(MSG_GET_READER_CAPABILITIES msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.GET_READER_CAPABILITIES_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_GET_READER_CAPABILITIES_RESPONSE) rsp;
        }
      

        /// <summary>
        /// ADD_ROSPEC message call.
        /// </summary>
        /// <param name="msg">MSG_ADD_ROSPEC to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_ADD_ROSPEC_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_ADD_ROSPEC_RESPONSE ADD_ROSPEC(MSG_ADD_ROSPEC msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.ADD_ROSPEC_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_ADD_ROSPEC_RESPONSE) rsp;
        }
      

        /// <summary>
        /// DELETE_ROSPEC message call.
        /// </summary>
        /// <param name="msg">MSG_DELETE_ROSPEC to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_DELETE_ROSPEC_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_DELETE_ROSPEC_RESPONSE DELETE_ROSPEC(MSG_DELETE_ROSPEC msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.DELETE_ROSPEC_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_DELETE_ROSPEC_RESPONSE) rsp;
        }
      

        /// <summary>
        /// START_ROSPEC message call.
        /// </summary>
        /// <param name="msg">MSG_START_ROSPEC to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_START_ROSPEC_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_START_ROSPEC_RESPONSE START_ROSPEC(MSG_START_ROSPEC msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.START_ROSPEC_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_START_ROSPEC_RESPONSE) rsp;
        }
      

        /// <summary>
        /// STOP_ROSPEC message call.
        /// </summary>
        /// <param name="msg">MSG_STOP_ROSPEC to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_STOP_ROSPEC_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_STOP_ROSPEC_RESPONSE STOP_ROSPEC(MSG_STOP_ROSPEC msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.STOP_ROSPEC_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_STOP_ROSPEC_RESPONSE) rsp;
        }
      

        /// <summary>
        /// ENABLE_ROSPEC message call.
        /// </summary>
        /// <param name="msg">MSG_ENABLE_ROSPEC to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_ENABLE_ROSPEC_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_ENABLE_ROSPEC_RESPONSE ENABLE_ROSPEC(MSG_ENABLE_ROSPEC msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.ENABLE_ROSPEC_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_ENABLE_ROSPEC_RESPONSE) rsp;
        }
      

        /// <summary>
        /// DISABLE_ROSPEC message call.
        /// </summary>
        /// <param name="msg">MSG_DISABLE_ROSPEC to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_DISABLE_ROSPEC_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_DISABLE_ROSPEC_RESPONSE DISABLE_ROSPEC(MSG_DISABLE_ROSPEC msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.DISABLE_ROSPEC_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_DISABLE_ROSPEC_RESPONSE) rsp;
        }
      

        /// <summary>
        /// GET_ROSPECS message call.
        /// </summary>
        /// <param name="msg">MSG_GET_ROSPECS to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_GET_ROSPECS_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_GET_ROSPECS_RESPONSE GET_ROSPECS(MSG_GET_ROSPECS msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.GET_ROSPECS_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_GET_ROSPECS_RESPONSE) rsp;
        }
      

        /// <summary>
        /// ADD_ACCESSSPEC message call.
        /// </summary>
        /// <param name="msg">MSG_ADD_ACCESSSPEC to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_ADD_ACCESSSPEC_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_ADD_ACCESSSPEC_RESPONSE ADD_ACCESSSPEC(MSG_ADD_ACCESSSPEC msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.ADD_ACCESSSPEC_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_ADD_ACCESSSPEC_RESPONSE) rsp;
        }
      

        /// <summary>
        /// DELETE_ACCESSSPEC message call.
        /// </summary>
        /// <param name="msg">MSG_DELETE_ACCESSSPEC to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_DELETE_ACCESSSPEC_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_DELETE_ACCESSSPEC_RESPONSE DELETE_ACCESSSPEC(MSG_DELETE_ACCESSSPEC msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.DELETE_ACCESSSPEC_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_DELETE_ACCESSSPEC_RESPONSE) rsp;
        }
      

        /// <summary>
        /// ENABLE_ACCESSSPEC message call.
        /// </summary>
        /// <param name="msg">MSG_ENABLE_ACCESSSPEC to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_ENABLE_ACCESSSPEC_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_ENABLE_ACCESSSPEC_RESPONSE ENABLE_ACCESSSPEC(MSG_ENABLE_ACCESSSPEC msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.ENABLE_ACCESSSPEC_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_ENABLE_ACCESSSPEC_RESPONSE) rsp;
        }
      

        /// <summary>
        /// DISABLE_ACCESSSPEC message call.
        /// </summary>
        /// <param name="msg">MSG_DISABLE_ACCESSSPEC to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_DISABLE_ACCESSSPEC_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_DISABLE_ACCESSSPEC_RESPONSE DISABLE_ACCESSSPEC(MSG_DISABLE_ACCESSSPEC msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.DISABLE_ACCESSSPEC_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_DISABLE_ACCESSSPEC_RESPONSE) rsp;
        }
      

        /// <summary>
        /// GET_ACCESSSPECS message call.
        /// </summary>
        /// <param name="msg">MSG_GET_ACCESSSPECS to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_GET_ACCESSSPECS_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_GET_ACCESSSPECS_RESPONSE GET_ACCESSSPECS(MSG_GET_ACCESSSPECS msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.GET_ACCESSSPECS_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_GET_ACCESSSPECS_RESPONSE) rsp;
        }
      

        /// <summary>
        /// GET_READER_CONFIG message call.
        /// </summary>
        /// <param name="msg">MSG_GET_READER_CONFIG to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_GET_READER_CONFIG_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_GET_READER_CONFIG_RESPONSE GET_READER_CONFIG(MSG_GET_READER_CONFIG msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.GET_READER_CONFIG_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_GET_READER_CONFIG_RESPONSE) rsp;
        }
      

        /// <summary>
        /// SET_READER_CONFIG message call.
        /// </summary>
        /// <param name="msg">MSG_SET_READER_CONFIG to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_SET_READER_CONFIG_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_SET_READER_CONFIG_RESPONSE SET_READER_CONFIG(MSG_SET_READER_CONFIG msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.SET_READER_CONFIG_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_SET_READER_CONFIG_RESPONSE) rsp;
        }
      

        /// <summary>
        /// CLOSE_CONNECTION message call.
        /// </summary>
        /// <param name="msg">MSG_CLOSE_CONNECTION to send to reader.</param>
        /// <param name="msg_err">MSG_ERROR_MESSAGE. output.</param>
        /// <param name="time_out">Fuction call time out in millisecond.</param>
        /// <returns>If the function is called successfully, return MSG_CLOSE_CONNECTION_RESPONSE. Otherwise, null is returned.</returns>
        public MSG_CLOSE_CONNECTION_RESPONSE CLOSE_CONNECTION(MSG_CLOSE_CONNECTION msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.CLOSE_CONNECTION_RESPONSE);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_CLOSE_CONNECTION_RESPONSE) rsp;
        }
      
        /// <summary>
        /// Get LLRP report
        /// </summary>
        /// <param name="msg">Message to be sent to reader.</param>
        /// <param name="msg_err">Error Message. output</param>
        /// <param name="time_out">Timeout in millisecond.</param>
        public void GET_REPORT(MSG_GET_REPORT msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            msg_err = null;
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.GET_REPORT);
            trans.Send(msg);
        }
      
        /// <summary>
        /// Keep alive acknowledgement
        /// </summary>
        /// <param name="msg">Message to be sent to reader.</param>
        /// <param name="msg_err">Error message. Output</param>
        /// <param name="time_out">Command timeout in millisecond.</param>
        public void KEEPALIVE_ACK(MSG_KEEPALIVE_ACK msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            msg_err = null;
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.KEEPALIVE_ACK);
            trans.Send(msg);
        }
      

        /// <summary>
        /// Enable events and reports
        /// </summary>
        /// <param name="msg">Message to be sent to reader.</param>
        /// <param name="msg_err">Error message.</param>
        /// <param name="time_out">Command time out in millisecond.</param>
        public void ENABLE_EVENTS_AND_REPORTS(MSG_ENABLE_EVENTS_AND_REPORTS msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            msg_err = null;
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.ENABLE_EVENTS_AND_REPORTS);
            trans.Send(msg);
        }
      
    }
    }
  