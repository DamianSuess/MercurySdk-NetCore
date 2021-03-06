<?xml version="1.0" encoding="UTF-8" ?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" 
	xmlns:llrp="http://www.llrp.org/ltk/schema/core/encoding/binary/1.0">
  <xsl:output omit-xml-declaration='yes' method='text' indent='yes'/>
  <xsl:template match="/llrp:llrpdef">
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
* Updates:
* 2022-04-06 [DJS]
*   - Removed reference to deprecated library, System.Runtime.Remoting
*   - Realigned tabbing and spacing for code cleanliness and errors
*   - Added back undocumented code (i.e. `OnGenericMessageReceived`) used by ThingMagic.Reader
***************************************************************************
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
//// using System.Runtime.Remoting;  // Not suppored in .NET Core
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
    public delegate void delegateGenericMessages(Message msg);    // UNDOCUMENTED

    //Delegates for sending encapsulated asyn. messages
    public delegate void delegateEncapReaderEventNotification(ENCAPED_READER_EVENT_NOTIFICATION msg);
    public delegate void delegateEncapRoAccessReport(ENCAPED_RO_ACCESS_REPORT msg);
    public delegate void delegateEncapKeepAlive(ENCAPED_KEEP_ALIVE msg);

    [Serializable]
    /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
    /// Device proxy for host application to connect with LLRP reader
    /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
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
        public event delegateGenericMessages OnGenericMessageReceived; // UNDOCUMENTED

        public event delegateEncapReaderEventNotification OnEncapedReaderEventNotification;
        public event delegateEncapRoAccessReport OnEncapedRoAccessReportReceived;
        public event delegateEncapKeepAlive OnEncapedKeepAlive;
        public event delegateGenericMessages OnEncapedGenericMessageReceived;  // UNDOCUMENTED

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
            catch
            {
            }
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
            catch
            {
            }
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
            catch
            {
            }
        }

        //// UNDOCUMENTED
        protected void TriggerGenericMessages(Message msg)
        {
            try
            {
                if (OnGenericMessageReceived != null) OnGenericMessageReceived(msg);
                if (OnEncapedGenericMessageReceived != null)
                {
                    Message genericMsg = msg;
                    OnEncapedGenericMessageReceived(genericMsg);
                }
            }
            catch { }
        }

        #region Properties
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// Reader name.
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
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

        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// Set LLRP message time out
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        public void SetMessageTimeOut(int time_out)
        {
            this.MSG_TIME_OUT = time_out;
        }

        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// Get LLRP message time out.
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        public int GetMessageTimeOut()
        {
            return MSG_TIME_OUT;
        }

        #endregion

        #region Methods

        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// LLRPClient constructor using default TCP port.
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        public LLRPClient() : this(5084)
        {
        }

        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// LLRPClient constructor with specified TCP port.
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="port"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>TCP communication port. int<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
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

        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// This thread runs as the sole LTKNET thread.  It's job is to
        /// dispatch asyncrhonous notifications to the client in a way
        /// that doesn't cause mis-ordering (beginInvoke) of events
        /// but also allows the client thread to call syncrhonous
        /// LTKNET APIs from their event handlers */
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
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
                            default:
                                TriggerGenericMessages(msg);  //// UNDOCUMENTED
                                break;
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    if ("Queue Closed" == ex.Message)
                    {
                    }
                    else
                        throw;
                }
            }
        }

        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// Open connection to LLRP reader.
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="llrp_reader_name"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Reader name, could either be IP address or DNS name. string<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="timeout"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Time out in millisecond for trying to connect to reader. int<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="status"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Connection attempt status.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>returns<xsl:text disable-output-escaping="yes">&gt;</xsl:text>True if the reader is opened without error.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/returns<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>exception cref="LLRPNetworkException"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Throw LLRPNetworkException when the network is unreable<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/exception<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
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

        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// Close LLRP connection.
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>returns<xsl:text disable-output-escaping="yes">&gt;</xsl:text>True if the connection is closed successfully.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/returns<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
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
                catch
                {
                }
	              finally
	              {
                    cI.OnFrameReceived -= new delegateMessageReceived(ProcessFrame);
                    connected = false;
	              }
            
	              try
	              {
                    notificationQueue.Close();
	              }
                catch
                {
                }

                return ret;
            }
            catch
            {
                return false;
            }
        }

        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// Implement IDisposible interface.
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
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
    <xsl:for-each select="llrp:messageDefinition">
      <xsl:choose>
        <xsl:when test="@name ='KEEPALIVE'">
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
        </xsl:when>
        <xsl:when test="@name = 'READER_EVENT_NOTIFICATION'">
                case ENUM_LLRP_MSG_TYPE.READER_EVENT_NOTIFICATION:
                    try
                    {
                        bArr = Util.ConvertByteArrayToBitArray(data);
                        length = bArr.Count;
                        MSG_READER_EVENT_NOTIFICATION ntf = MSG_READER_EVENT_NOTIFICATION.FromBitArray(ref bArr, ref cursor, length);
                        if (conn_evt != null <xsl:text disable-output-escaping="yes">&amp;&amp;</xsl:text> ntf.ReaderEventNotificationData.ConnectionAttemptEvent != null)
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
        </xsl:when>
        <xsl:when test="@name = 'RO_ACCESS_REPORT'">
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
        </xsl:when>        
      </xsl:choose>
    </xsl:for-each>
                default:
                    //// UNDOCUMENTED
                    bArr = Util.ConvertByteArrayToBitArray(data);
                    length = bArr.Count;
                    Message gMesg = null;
                    switch ((ENUM_LLRP_MSG_TYPE)msg_type)
                    {
                        case ENUM_LLRP_MSG_TYPE.GET_READER_CONFIG_RESPONSE:
                            gMesg = MSG_GET_READER_CONFIG_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.GET_READER_CAPABILITIES_RESPONSE:
                            gMesg = MSG_GET_READER_CAPABILITIES_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.DELETE_ROSPEC_RESPONSE:
                            gMesg = MSG_DELETE_ROSPEC_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.DELETE_ACCESSSPEC_RESPONSE:
                            gMesg = MSG_DELETE_ACCESSSPEC_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.STOP_ROSPEC_RESPONSE:
                            gMesg = MSG_STOP_ROSPEC_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.GET_ROSPECS_RESPONSE:
                            gMesg = MSG_GET_ROSPECS_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.SET_READER_CONFIG_RESPONSE:
                            gMesg = MSG_SET_READER_CONFIG_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.START_ROSPEC_RESPONSE:
                            gMesg = MSG_START_ROSPEC_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.ADD_ROSPEC_RESPONSE:
                            gMesg = MSG_ADD_ROSPEC_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.ENABLE_ROSPEC_RESPONSE:
                            gMesg = MSG_ENABLE_ROSPEC_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.DISABLE_ROSPEC_RESPONSE:
                            gMesg = MSG_DISABLE_ROSPEC_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.GET_ACCESSSPECS_RESPONSE:
                            gMesg = MSG_GET_ACCESSSPECS_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.ADD_ACCESSSPEC_RESPONSE:
                            gMesg = MSG_ADD_ACCESSSPEC_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.ENABLE_ACCESSSPEC_RESPONSE:
                            gMesg = MSG_ENABLE_ACCESSSPEC_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.CLOSE_CONNECTION_RESPONSE:
                            gMesg = MSG_CLOSE_CONNECTION_RESPONSE.FromBitArray(ref bArr, ref cursor, length);
                            break;
                        case ENUM_LLRP_MSG_TYPE.CUSTOM_MESSAGE:
                            // First decode it as custom message.
                            BitArray tempArr = bArr;
                            int tempCursor = cursor;
                            MSG_CUSTOM_MESSAGE mesg = MSG_CUSTOM_MESSAGE.FromBitArray(ref tempArr, ref tempCursor, length);
                            // Based on subtype , decode the actual type of custom message.
                            switch ( mesg.SubType )
                            {
                                case (byte)2:
                                    gMesg = Org.LLRP.LTK.LLRPV1.thingmagic.MSG_THINGMAGIC_CONTROL_RESPONSE_POWER_CYCLE_READER.FromBitArray(ref bArr, ref cursor, length);
                                    break;
                                case (byte)4:
                                    gMesg = Org.LLRP.LTK.LLRPV1.thingmagic.MSG_THINGMAGIC_CONTROL_RESPONSE_RESET_STATISTICS.FromBitArray(ref bArr, ref cursor, length);
                                    break;
                                case (byte)6:
                                    gMesg = Org.LLRP.LTK.LLRPV1.thingmagic.MSG_THINGMAGIC_CONTROL_RESPONSE_GET_RESET_TIME.FromBitArray(ref bArr, ref cursor, length);
                                    break;
                                case (byte)8:
                                    gMesg = Org.LLRP.LTK.LLRPV1.thingmagic.MSG_THINGMAGIC_CONTROL_RESPONSE_GET_ANTENNA_STATS.FromBitArray(ref bArr, ref cursor, length);
                                    break;
                                case (byte)12:
                                    gMesg = Org.LLRP.LTK.LLRPV1.thingmagic.MSG_THINGMAGIC_CONTROL_RESPONSE_GET_READER_STATUS.FromBitArray(ref bArr, ref cursor, length);
                                    break;
                                default:
                                    gMesg = MSG_CUSTOM_MESSAGE.FromBitArray(ref bArr, ref cursor, length);
                                    break;
                            }
                            break;
                        default:
                            break;
                    }
                    notificationQueue.Enqueue(gMesg);
                    break;
            }
        }
        #endregion
    <xsl:for-each select="llrp:messageDefinition">
      <xsl:variable name="msgName">
        <xsl:value-of select="@name"/>
      </xsl:variable>
      <xsl:variable name="shorten_msgName">
        <xsl:if test="contains($msgName, '_RESPONSE')">
          <xsl:value-of select="substring-before($msgName, '_RESPONSE')"/>
        </xsl:if>
        <xsl:if test="contains($msgName, '_ACK')">
          <xsl:value-of select="substring-before($msgName, '_ACK')"/>
        </xsl:if>
      </xsl:variable>
      <xsl:if test ="contains($msgName, '_RESPONSE')">
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:value-of select="$shorten_msgName"/> message call.
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="msg"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>MSG_<xsl:value-of select="$shorten_msgName"/> to send to reader.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="msg_err"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>MSG_ERROR_MESSAGE. output.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="time_out"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Fuction call time out in millisecond.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>returns<xsl:text disable-output-escaping="yes">&gt;</xsl:text>If the function is called successfully, return MSG_<xsl:value-of select="@name"/>. Otherwise, null is returned.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/returns<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        public MSG_<xsl:value-of select="@name"/><xsl:text> </xsl:text> <xsl:value-of select="$shorten_msgName"/>(MSG_<xsl:value-of select="$shorten_msgName"/> msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.<xsl:value-of select="@name"/>);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_<xsl:value-of select="@name"/>) rsp;
        }
      </xsl:if>
      <xsl:if test="contains($msgName, 'ENABLE_EVENTS_AND_REPORTS')">
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// Enable events and reports
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="msg"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Message to be sent to reader.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="msg_err"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Error message.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="time_out"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Command time out in millisecond.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        public void <xsl:value-of select="$msgName"/>(MSG_<xsl:value-of select="$msgName"/> msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            msg_err = null;
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.<xsl:value-of select="$msgName"/>);
            trans.Send(msg);
        }
      </xsl:if>
      <xsl:if test="contains($msgName, 'KEEPALIVE_ACK')">
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// Keep alive acknowledgement
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="msg"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Message to be sent to reader.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="msg_err"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Error message. Output<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="time_out"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Command timeout in millisecond.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        public void <xsl:value-of select="$msgName"/>(MSG_<xsl:value-of select="$msgName"/> msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            msg_err = null;
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.<xsl:value-of select="$msgName"/>);
            trans.Send(msg);
        }
      </xsl:if>
      <xsl:if test="@name='GET_REPORT'">
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// Get LLRP report
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="msg"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Message to be sent to reader.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="msg_err"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Error Message. output<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="time_out"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Timeout in millisecond.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        public void <xsl:value-of select="$msgName"/>(MSG_<xsl:value-of select="$msgName"/> msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            msg_err = null;
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.<xsl:value-of select="$msgName"/>);
            trans.Send(msg);
        }
      </xsl:if>      
      <xsl:if test="contains($msgName, 'CUSTOM_MESSAGE')">
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// Send Customized Message to Reader
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>/summary<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="msg"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Message to be sent to reader.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="msg_err"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Error message. output<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>param name="time_out"<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Timeout in millisecond.<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/param<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        /// <xsl:text disable-output-escaping="yes">&lt;</xsl:text>returns<xsl:text disable-output-escaping="yes">&gt;</xsl:text>Custom Message<xsl:text disable-output-escaping="yes">&lt;</xsl:text>/returns<xsl:text disable-output-escaping="yes">&gt;</xsl:text>
        public MSG_CUSTOM_MESSAGE<xsl:text>  </xsl:text><xsl:value-of select="$msgName"/>(MSG_<xsl:value-of select="$msgName"/> msg, out MSG_ERROR_MESSAGE msg_err, int time_out)
        {
            Transaction trans = new Transaction(cI, msg.MSG_ID, ENUM_LLRP_MSG_TYPE.<xsl:value-of select="$msgName"/>);
            Message rsp = trans.Transact(msg, out msg_err, time_out);
            return (MSG_<xsl:value-of select="$msgName"/>) rsp;
        }
      </xsl:if>
    </xsl:for-each>
    }
}
  </xsl:template>
</xsl:stylesheet>