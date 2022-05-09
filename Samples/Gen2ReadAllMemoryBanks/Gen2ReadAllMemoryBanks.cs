using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;

namespace Gen2ReadAllMemoryBanks
{
    /// <summary>
    /// Sample program that reads tags for a fixed period of time (500ms)
    /// and prints the tags found.
    /// </summary>
    class Gen2ReadAllMemoryBanks
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
        public Reader reader = null;
        public static int[] antennaList = null;
        static void Main(string[] args)
        {
            // Program setup
            if (1 > args.Length)
            {
                Usage();
            }
            
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
                    if (Reader.Region.UNSPEC == (Reader.Region)r.ParamGet("/reader/region/id"))
                    {
                        Reader.Region[] supportedRegions = (Reader.Region[])r.ParamGet("/reader/region/supportedRegions");
                        if (supportedRegions.Length < 1)
                        {
                            throw new FAULT_INVALID_REGION_Exception();
                        }
                        r.ParamSet("/reader/region/id", supportedRegions[0]);
                    }
                    Gen2ReadAllMemoryBanks prgm = new Gen2ReadAllMemoryBanks();
                    prgm.reader = r;

                    // Read Plan
                    byte length;
                    string model = (string)r.ParamGet("/reader/version/model");
                    if (r.isAntDetectEnabled(antennaList))
                    {
                        Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                        Usage();
                    }
                    if ("M6e".Equals(model)
                        || "M6e PRC".Equals(model)
                        || "M6e JIC".Equals(model)
                        || "M6e Micro".Equals(model)
                        || "Mercury6".Equals(model)
                        || "Sargas".Equals(model)
                        || "Astra-EX".Equals(model))
                    {
                        // Specifying the readLength = 0 will return full TID for any tag read in case of M6e varients, M6 and Astra-EX reader.
                        length = 0;
                    }
                    else
                    {
                        length = 2;
                    }
                    prgm.PerformWriteOperation();

                    SimpleReadPlan srp = new SimpleReadPlan(antennaList, TagProtocol.GEN2, null, null, 1000);
                    r.ParamSet("/reader/read/plan",srp);

                    TagReadData[] tagReadsFilter = r.Read(500);

                    if (tagReadsFilter.Length == 0)
                    {
                        Console.WriteLine("No tags found");
                        return;
                    }

                    TagFilter filter = new TagData(tagReadsFilter[0].EpcString);
                    TagOp op;
                    Console.WriteLine("Perform embedded and standalone tag operation - read only user memory without filter");
                    Console.WriteLine();
                    op = new Gen2.ReadData(Gen2.Bank.USER, 0, length);
                    prgm.PerformReadAllMemOperation(null, op);
                    Console.WriteLine();

                    Console.WriteLine("Perform embedded and standalone tag operation - read only user memory with filter");
                    Console.WriteLine();
                    op = null;
                    op = new Gen2.ReadData(Gen2.Bank.USER, 0, length);
                    prgm.PerformReadAllMemOperation(filter, op);
                    Console.WriteLine();


                    Console.WriteLine("Perform embedded and standalone tag operation - read user memory, reserved memory, tid memory and epc memory without filter");
                    Console.WriteLine();
                    op = null;
                    op = new Gen2.ReadData(Gen2.Bank.USER | Gen2.Bank.GEN2BANKUSERENABLED | Gen2.Bank.GEN2BANKRESERVEDENABLED | Gen2.Bank.GEN2BANKEPCENABLED | Gen2.Bank.GEN2BANKTIDENABLED, 0, length);
                    prgm.PerformReadAllMemOperation(null, op);
                    Console.WriteLine();

                    Console.WriteLine("Perform embedded and standalone tag operation - read user memory, reserved memory with filter");
                    Console.WriteLine();
                    op = null;
                    op = new Gen2.ReadData(Gen2.Bank.USER | Gen2.Bank.GEN2BANKUSERENABLED | Gen2.Bank.GEN2BANKRESERVEDENABLED, 0, length);
                    prgm.PerformReadAllMemOperation(filter, op);
                    Console.WriteLine();

                    Console.WriteLine("Perform embedded and standalone tag operation - read user memory, reserved memory and tid memory with filter");
                    Console.WriteLine();
                    op = null;
                    op = new Gen2.ReadData(Gen2.Bank.USER | Gen2.Bank.GEN2BANKUSERENABLED | Gen2.Bank.GEN2BANKRESERVEDENABLED | Gen2.Bank.GEN2BANKTIDENABLED, 0, length);
                    prgm.PerformReadAllMemOperation(filter, op);
                    Console.WriteLine();

                    Console.WriteLine("Perform embedded and standalone tag operation - read user memory, reserved memory, tid memory and epc memory with filter");
                    Console.WriteLine();
                    op = null;
                    op = new Gen2.ReadData(Gen2.Bank.USER | Gen2.Bank.GEN2BANKUSERENABLED | Gen2.Bank.GEN2BANKRESERVEDENABLED | Gen2.Bank.GEN2BANKEPCENABLED | Gen2.Bank.GEN2BANKTIDENABLED, 0, length);
                    prgm.PerformReadAllMemOperation(filter, op);
                    Console.WriteLine();
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

        #region PerformReadAllMemOperation

        private void PerformReadAllMemOperation(TagFilter filter, TagOp op)
        {
            TagReadData[] tagReads = null;
            SimpleReadPlan plan = new SimpleReadPlan(antennaList, TagProtocol.GEN2, filter, op, 1000);
            reader.ParamSet("/reader/read/plan", plan);
            Console.WriteLine("Embedded tag operation - ");
            // Read tags
            tagReads = reader.Read(500);
            foreach (TagReadData tr in tagReads)
            {
                Console.WriteLine(tr.ToString());
                if (0 < tr.Data.Length)
                {
                    Console.WriteLine(" Embedded read data: " + ByteFormat.ToHex(tr.Data, "", " "));
                    Console.WriteLine(" User memory: " + ByteFormat.ToHex(tr.USERMemData, "", " "));
                    Console.WriteLine(" Reserved memory: " + ByteFormat.ToHex(tr.RESERVEDMemData, "", " "));
                    Console.WriteLine(" Tid memory: " + ByteFormat.ToHex(tr.TIDMemData, "", " "));
                    Console.WriteLine(" EPC memory: " + ByteFormat.ToHex(tr.EPCMemData, "", " "));
                }
                Console.WriteLine(" Embedded read data length:" + tr.Data.Length);
            }
            Console.WriteLine();
            Console.WriteLine("Standalone tag operation - ");
            //Use first antenna for operation
            if (antennaList != null)
                reader.ParamSet("/reader/tagop/antenna", antennaList[0]);

            ushort[] data = (ushort[]) reader.ExecuteTagOp(op, filter);
            //// Print tag reads
            if (0 < data.Length)
            {
                Console.WriteLine(" Standalone read data:" +
                                  ByteFormat.ToHex(ByteConv.ConvertFromUshortArray(data), "", " "));
                Console.WriteLine(" Standalone read data length:" + ByteConv.ConvertFromUshortArray(data).Length);
            }
            data = null;
            Console.WriteLine();
        }

        #endregion

        #region PerformWriteOperation

        private void PerformWriteOperation()
        {
            //Use first antenna for operation
            if (antennaList != null)
                reader.ParamSet("/reader/tagop/antenna", antennaList[0]);

            Gen2.TagData epc = new Gen2.TagData(new byte[]
            {
                0x01, 0x23, 0x45, 0x67, 0x89, 0xAB,
                0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67,
            });
            Console.WriteLine("Write on epc mem: " + epc.EpcString);
            Gen2.WriteTag tagop = new Gen2.WriteTag(epc);
            reader.ExecuteTagOp(tagop, null);
            Console.WriteLine();

            ushort[] data = new ushort[] { 0x1234, 0x5678 };
            Console.WriteLine("Write on reserved mem: " + ByteFormat.ToHex(data));
            Gen2.BlockWrite blockwrite = new Gen2.BlockWrite(Gen2.Bank.RESERVED, 0, data);
            reader.ExecuteTagOp(blockwrite, null);
            Console.WriteLine();

            data = null;
            data = new ushort[] { 0xFFF1, 0x1122 };
            Console.WriteLine("Write on user mem: " + ByteFormat.ToHex(data));
            blockwrite = new Gen2.BlockWrite(Gen2.Bank.USER, 0, data);
            reader.ExecuteTagOp(blockwrite, null);
            Console.WriteLine();
        }

        #endregion

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
