﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Org.LLRP.LTK.LLRPV1;
using Org.LLRP.LTK.LLRPV1.DataType;

namespace LLRP2LLRP
{
    class LLRP2LLRP
    {
        private static void read_msg(Stream source, byte[] hdr, ref LLRPBinaryDecoder.LLRP_Envelope env, out byte[] packet)
        {
            /* read remaining bytes */
            if (env.msg_len < LLRPBinaryDecoder.MIN_HDR || env.msg_len > 4000000)
            {
                throw new MalformedPacket("Message length (" + env.msg_len + ") out-of-range");
            }

            int remainder = (int)env.msg_len - LLRPBinaryDecoder.MIN_HDR;
            packet = new byte[env.msg_len];
            Array.Copy(hdr, packet, LLRPBinaryDecoder.MIN_HDR);
            int bytes_read = source.Read(packet, LLRPBinaryDecoder.MIN_HDR, remainder);
            if (bytes_read < remainder)
            {
                throw new MalformedPacket("Reached EOF before end of message");
            }
        }

        static void Main(string[] args)
        {
            Stream outp;
            Stream inp;
            int msg_no = 0;

            if (args.Length == 2)
            {
                inp = new FileStream(args[0], FileMode.Open, FileAccess.Read);
                outp = new FileStream(args[1], FileMode.OpenOrCreate, FileAccess.Write);
            }
            else if (args.Length == 1)
            {
                inp = new FileStream(args[0], FileMode.Open, FileAccess.Read);
                outp = Console.OpenStandardOutput();
            }
            else
            {
                inp = Console.OpenStandardInput();
                outp = Console.OpenStandardOutput();
            }

            while (true)
            {
                /* read message header */
                byte[] hdr = new byte[LLRPBinaryDecoder.MIN_HDR];
                int how_many = inp.Read(hdr, 0, LLRPBinaryDecoder.MIN_HDR);
                if (how_many == 0)
                {
                    break;
                }
                else if (how_many < LLRPBinaryDecoder.MIN_HDR)
                {
                    throw new MalformedPacket("Header fragment at end-of-file");
                }

                /* get the full message */
                LLRPBinaryDecoder.LLRP_Envelope env;
                LLRPBinaryDecoder.Decode_Envelope(hdr, out env);

                byte[] packet;
                read_msg(inp, hdr, ref env, out packet);
                Message msg;

                try
                {
                    LLRPBinaryDecoder.Decode(ref packet, out msg);
                }
                catch (Exception e)
                {
                    String desc = "Decode failure on Packet #" + msg_no + ", " + e.Message;
                    Console.Error.WriteLine(desc);

                    String err_msg =
                        "<ERROR_MESSAGE MessageID=\"0\" Version=\"0\">\r\n" +
                        "  <LLRPStatus>\r\n" +
                        "    <StatusCode>R_DeviceError</StatusCode>\r\n" +
                        "    <ErrorDescription>" + desc + "</ErrorDescription>\r\n" +
                        "  </LLRPStatus>\r\n" +
                        "</ERROR_MESSAGE>\r\n";

                    ENUM_LLRP_MSG_TYPE dummy;
                    LLRPXmlParser.ParseXMLToLLRPMessage(err_msg, out msg, out dummy);
                }

                try
                {
                    packet = Util.ConvertBitArrayToByteArray(msg.ToBitArray());
                    outp.Write(packet, 0, packet.Length);
                }
                catch (Exception e)
                {
                    String desc = "Encode failure on packet #" + msg_no + ", " + e.Message;
                    Console.Error.WriteLine(desc);
                }
            }

            msg_no++;
        }
    }
}

