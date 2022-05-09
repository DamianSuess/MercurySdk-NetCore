using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;

namespace PassThroughDemo
{
    /// <summary>
    /// Sample program that demonstrates the passthrough functionality
    /// </summary>
    class Program
    {
        public static byte OPCODE_SELECT_TAG = 0x25;
        public static byte GET_RANDOM_NUMBER = 0xb2;
        public static byte IC_MFG_CODE_NXP = 0x04;
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/'",
                    " Example for UHF: 'tmr:///com4' or 'tmr:///com4 --ant 1,2' or '-v tmr:///com4 --ant 1,2'",
                    " Example for HF/LF: 'tmr:///com4'"
                }));
            Environment.Exit(1);
        }
        static void Main(string[] args)
        {
            uint timeout = 0;
            uint configFlags = 0;
            List<byte> buffer = new List<byte>();
            byte flags = 0;
            PassThrough passThroughOp;
            byte[] passThroughResp;

            // Program setup
            if (1 > args.Length)
            {
                Usage();
            }

            try
            {
                // Create Reader object, connecting to physical device.
                // Wrap reader in a "using" block to get automatic
                // reader shutdown (using IDisposable interface).
                using (Reader r = Reader.Create(args[0]))
                {
                    //Uncomment this line to add default transport listener.
                    //r.Transport += r.SimpleTransportListener;

                    r.Connect();

                    // Perform sync read for 500ms
                    SimpleReadPlan plan = new SimpleReadPlan(null, TagProtocol.ISO15693, null, null, 1000);
                    // Set the created readplan
                    r.ParamSet("/reader/read/plan", plan);
                    // Read tags
                    TagReadData[] tagReads = r.Read(500);
                    // Print first tag read epc
                    byte[] epc = tagReads[0].Epc;
                    Console.WriteLine("Tag ID: " + tagReads[0].EpcString);

                    //Select Tag
                    timeout = 500; //timeout in milliseconds.
                    flags = 0x22;
                    configFlags = (uint)(ConfigFlags.ENABLE_TX_CRC | ConfigFlags.ENABLE_RX_CRC | ConfigFlags.ENABLE_INVENTORY);
                    //Frame payload data as per 15693 protocol(ICODE Slix-S)
                    buffer.Add(flags);
                    buffer.Add(OPCODE_SELECT_TAG);

                    //Append UID(reverse).
                    buffer.AddRange(appendReverseUID(epc));

                    //Execute passthrough tag op to select a tag
                    passThroughOp = new PassThrough(timeout, configFlags, buffer);
                    passThroughResp = (byte[])r.ExecuteTagOp(passThroughOp, null);
                    if (passThroughResp.Length > 0)
                    {
                        Console.WriteLine("Select Tag| Data(" + passThroughResp.Length + "): " + ByteFormat.ToHex(passThroughResp,"",""));
                    }

                    //Reset command buffer
                    buffer.Clear();

                    //Get random number
                    // Initialize passthrough tag operation with all the required fields
                    flags = 0x12;
                    configFlags = (uint)(ConfigFlags.ENABLE_TX_CRC | ConfigFlags.ENABLE_RX_CRC | ConfigFlags.ENABLE_INVENTORY);
                    //Extract random number from response(ICODE Slix-S)
                    buffer.Add(flags);
                    buffer.Add(GET_RANDOM_NUMBER);
                    buffer.Add(IC_MFG_CODE_NXP);

                    passThroughOp = new PassThrough(timeout, configFlags, buffer);
                    passThroughResp = (byte[])r.ExecuteTagOp(passThroughOp, null);

                    if (passThroughResp.Length > 0)
                    {
                        Console.WriteLine("RN number |  Data(" + passThroughResp.Length + "): " + ByteFormat.ToHex(passThroughResp,"",""));
                    }
                }
            }
            catch (ReaderException re)
            {
                Console.WriteLine("Error: " + re.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }

        public static List<byte> appendReverseUID(byte[] uidISO15693)
        {
            List<byte> reversedUid = new List<byte>();
            int length = uidISO15693.Length;
            int i = 0;
            while (i < length)
            {
                reversedUid.Add(uidISO15693[(length - 1) - i]);
                i++;
            }
            return reversedUid;
        }
    }
}