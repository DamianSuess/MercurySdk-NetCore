using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;

namespace BlockWrite
{
    /// <summary>
    /// Sample program that perform BlockWrite
    /// </summary>
    class BlockWrite
    {
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--ant n[,n...]]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " [--ant n[,n...]] : e.g., '--ant 1,2,..,n",
                    " Example: 'tmr:///com4' or 'tmr:///com4 --ant 1,2' or '-v tmr:///com4 --ant 1,2'"
            }));
            Environment.Exit(1);
        }
        static void Main(string[] args)
        {
            // Program setup
            if (1 > args.Length)
            {
                Usage();
            }
            int[] antennaList = null;
            for (int nextarg = 1; nextarg < args.Length; nextarg++)
            {
                string arg = args[nextarg];
                if (arg.Equals("--ant"))
                {
                    if (null != antennaList)
                    {
                        Console.WriteLine("Duplicate argument: --ant specified more than once");
                        Usage();
                    }
                    antennaList = ParseAntennaList(args, nextarg);
                    nextarg++;
                }
                else
                {
                    Console.WriteLine("Argument {0}:\"{1}\" is not recognized", nextarg, arg);
                    Usage();
                }
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
                    if ((r is SerialReader) || (r is LlrpReader))
                    {
                        if (Reader.Region.UNSPEC == (Reader.Region)r.ParamGet("/reader/region/id"))
                        {
                            Reader.Region[] supportedRegions = (Reader.Region[])r.ParamGet("/reader/region/supportedRegions");
                            if (supportedRegions.Length < 1)
                            {
                                throw new FAULT_INVALID_REGION_Exception();
                            }
                            r.ParamSet("/reader/region/id", supportedRegions[0]);
                        }
                        if (r.isAntDetectEnabled(antennaList))
                        {
                            Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                            Usage();
                        }
                        Gen2.Password pass = new Gen2.Password(0x0);
                        r.ParamSet("/reader/gen2/accessPassword", pass);

                        //Use first antenna for operation
                        if (antennaList != null)
                            r.ParamSet("/reader/tagop/antenna", antennaList[0]);

                        // BlockWrite and read using ExecuteTagOp

                        Gen2.BlockWrite blockwrite = new Gen2.BlockWrite(Gen2.Bank.USER, 0, new ushort[] { 0xFFF1, 0x1122 });
                        r.ExecuteTagOp(blockwrite, null);

                        Gen2.ReadData read = new Gen2.ReadData(Gen2.Bank.USER, 0, 2);
                        ushort[] readData = (ushort[])r.ExecuteTagOp(read, null);

                        foreach (ushort word in readData)
                        {
                            Console.Write(" {0:X4}", word);
                        }
                        Console.WriteLine();


                        // BlockWrite and read using embedded read command

                        SimpleReadPlan readplan;
                        TagReadData[] tagreads;

                        blockwrite = new Gen2.BlockWrite(Gen2.Bank.USER, 0, new ushort[] { 0x1234, 0x5678 });
                        readplan = new SimpleReadPlan(antennaList, TagProtocol.GEN2,
                            //null,
                            new TagData("1234567890ABCDEF"),
                            blockwrite, 0);
                        r.ParamSet("/reader/read/plan", readplan);
                        r.Read(500);

                        readplan = new SimpleReadPlan(antennaList, TagProtocol.GEN2,
                            null,
                            new Gen2.ReadData(Gen2.Bank.USER, 0, 2),
                            0);
                        r.ParamSet("/reader/read/plan", readplan);
                        tagreads = r.Read(500);
                        foreach (TagReadData trd in tagreads)
                        {
                            foreach (byte b in trd.Data)
                            {
                                Console.Write(" {0:X2}", b);
                            }
                            Console.WriteLine("    " + trd);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Error: This codelet works only for Serial Readers and Llrp Readers");
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

        #region ParseAntennaList

        private static int[] ParseAntennaList(IList<string> args, int argPosition)
        {
            int[] antennaList = null;
            try
            {
                string str = args[argPosition + 1];
                antennaList = Array.ConvertAll<string, int>(str.Split(','), int.Parse);
                if (antennaList.Length == 0)
                {
                    antennaList = null;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("Missing argument after args[{0:d}] \"{1}\"", argPosition, args[argPosition]);
                Usage();
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0}\"{1}\"", ex.Message, args[argPosition + 1]);
                Usage();
            }
            return antennaList;
        }

        #endregion

    }
}
