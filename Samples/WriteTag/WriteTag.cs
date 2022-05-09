//Uncomment this to enable filter
//#define ENABLE_FILTER
//Uncomment this to enable readafterwrite functionality
//#define ENABLE_READ_AFTER_WRITE 
//Uncomment this to disable M3e filter functionality
//#define ENABLE_M3E_FILTER
//Comment this to disable M3e blockwrite and blockread functionality
#define ENABLE_M3E_BLOCK_READ_WRITE
//Uncomment this to enable get system information for M3e
//#define ENABLE_M3E_SYSTEM_INFORMATION_MEMORY
//Uncomment this to enable block protection status memory for M3e
//#define ENABLE_M3E_BLOCK_PROTECTION_STATUS
//Uncomment this to enable secure id for M3e
//#define ENABLE_M3E_SECURE_ID

using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;

namespace WriteTag
{
    /// <summary>
    /// Sample program that writes an EPC to a tag and demonstrates the functionality of read after write
    /// </summary>
    class WriteTag
    {
        static int[] _antennaList = null;
        static Reader _r = null;
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--ant n[,n...]]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " [--ant n[,n...]] : e.g., '--ant 1,2,..,n",
                    " Example for UHF: 'tmr:///com4' or 'tmr:///com4 --ant 1,2' or '-v tmr:///com4 --ant 1,2'",
                    " Example for HF/LF: 'tmr:///com4'"
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

            for (int nextarg = 1; nextarg < args.Length; nextarg++)
            {
                string arg = args[nextarg];
                if (arg.Equals("--ant"))
                {
                    if (null != _antennaList)
                    {
                        Console.WriteLine("Duplicate argument: --ant specified more than once");
                        Usage();
                    }

                    _antennaList = ParseAntennaList(args, nextarg);
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
                using (_r = Reader.Create(args[0]))
                {
                    //Uncomment this line to add default transport listener.
                    //r.Transport += r.SimpleTransportListener;

                    _r.Connect();
                    if (Reader.Region.UNSPEC == (Reader.Region)_r.ParamGet("/reader/region/id"))
                    {
                        Reader.Region[] supportedRegions = (Reader.Region[])_r.ParamGet("/reader/region/supportedRegions");
                        if (supportedRegions.Length < 1)
                        {
                            throw new FAULT_INVALID_REGION_Exception();
                        }

                        _r.ParamSet("/reader/region/id", supportedRegions[0]);
                    }

                    string model = (string)_r.ParamGet("/reader/version/model").ToString();
                    if (!model.Equals("M3e"))
                    {
                        if (_r.isAntDetectEnabled(_antennaList))
                        {
                            Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                            Usage();
                        }
                        //Use first antenna for operation
                        if (_antennaList != null)
                            _r.ParamSet("/reader/tagop/antenna", _antennaList[0]);
                    }
                    else
                    {
                        if (_antennaList != null)
                        {
                            Console.WriteLine("Module doesn't support antenna input");
                            Usage();
                        }
                    }

                    // This select filter matches all Gen2 tags where bits 32-48 of the EPC are 0x0123 
#if ENABLE_FILTER
                    TagFilter filter = new Gen2.Select(false, Gen2.Bank.EPC, 32, 16, new byte[] { 0x01, 0x23});
                   
#endif
                    if (!model.Equals("M3e"))
                    {
                        //Gen2.TagData epc = new Gen2.TagData(new byte[] {
                        //    0x01, 0x23, 0x45, 0x67, 0x89, 0xAB,
                        //    0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67,
                        //});
                        //Gen2.WriteTag tagop = new Gen2.WriteTag(epc);
                        //r.ExecuteTagOp(tagop, null);
                    }

                    // Reads data from a tag memory bank after writing data to the requested memory bank without powering down of tag
#if ENABLE_READ_AFTER_WRITE
                    {
                        //create a tagopList with write tagop followed by read tagop
                        TagOpList tagopList = new TagOpList();
                        byte wordCount;
                        ushort[] readData;

                        //Write one word of data to USER memory and read back 8 words from EPC memory using WriteData and ReadData
                        {
                            ushort[] writeData = { 0x9999 };
                            wordCount = 8;
                            Gen2.WriteData wData = new Gen2.WriteData(Gen2.Bank.USER, 2, writeData);
                            Gen2.ReadData rData = new Gen2.ReadData(Gen2.Bank.EPC, 0, wordCount);
                            //Gen2.WriteTag wTag = new Gen2.WriteTag(epc);

                            // assemble tagops into list
                            tagopList.list.Add(wData);
                            tagopList.list.Add(rData);

                            Console.WriteLine("###################Embedded Read after write######################");
                            // uncomment the following for embedded read after write.
                            embeddedRead(TagProtocol.GEN2 ,null, tagopList);

                            // call executeTagOp with list of tagops
                            //readData = (ushort[])r.ExecuteTagOp(tagopList, null);
                            //Console.WriteLine("ReadData: ");
                            //foreach (ushort word in readData)
                            //{
                            //    Console.Write(" {0:X4}", word);
                            //}
                            //Console.WriteLine("\n");
                        }

                        //clearing the list for next operation
                        tagopList.list.Clear();

                        //Write 12 bytes(6 words) of EPC and read back 8 words from EPC memory using WriteTag and ReadData
                        {
                            Gen2.TagData epc1 = new Gen2.TagData(new byte[] {
                                 0x11, 0x22, 0x33, 0x44, 0x55, 0x66,
                                 0x77, 0x88, 0x99, 0xaa, 0xbb, 0xcc,
                            });
                            wordCount = 8;
                            Gen2.WriteTag wtag = new Gen2.WriteTag(epc1);
                            Gen2.ReadData rData = new Gen2.ReadData(Gen2.Bank.EPC, 0, wordCount);

                            // assemble tagops into list
                            tagopList.list.Add(wtag);
                            tagopList.list.Add(rData);

                            // call executeTagOp with list of tagops
                            //readData = (ushort[])r.ExecuteTagOp(tagopList, null);
                            //Console.WriteLine("ReadData: ");
                            //foreach (ushort word in readData)
                            //{
                            //    Console.Write(" {0:X4}", word);
                            //}
                            //Console.WriteLine("\n");

                           
                        }
                    }
#endif

                    // Perform read and print UID and tagtype of tag found
                    SimpleReadPlan plan = new SimpleReadPlan(_antennaList, TagProtocol.ISO15693, null, null, 1000);
                    _r.ParamSet("/reader/read/plan", plan);
                    TagReadData[] tagReads = _r.Read(1000);
                    Console.WriteLine("UID: " + tagReads[0].EpcString);
                    Console.WriteLine("TagType:  " + (Iso15693.TagType)(tagReads[0].TagType));
                    MemoryType type;
                    MultiFilter mulfilter = null;
                    UInt32 address;
                    byte length;

#if ENABLE_M3E_FILTER
                    //Initialize filter
                    // Filters the tag based on tagtype
                    TagFilter tagTypeFilter = new Select_TagType((UInt64)((Iso15693.TagType)(tagReads[0].TagType)));
                    // Filters the tag based on UID
                    TagFilter uidFilter = new Select_UID(32, ByteFormat.FromHex(tagReads[0].Tag.EpcString.Substring(0, 8)));
                    // Initialize multi filter
                    mulfilter = new MultiFilter(new TagFilter[] { tagTypeFilter, uidFilter });
#endif

#if ENABLE_M3E_BLOCK_READ_WRITE

                    //Initialize all the fields required for Read Data Tag operation
                    type = MemoryType.BLOCK_MEMORY;
                    address = 0;
                    length = 1;

                    // Read memory before write
                    ReadMemory bRead = new ReadMemory(type, address, length);
                    byte[] dataRead = (byte[])_r.ExecuteTagOp(bRead, mulfilter);

                    // prints the data read
                    Console.WriteLine("Read Data before performing block write: ");
                    foreach (byte i in dataRead)
                    {
                        Console.Write(" {0:X2}", i);
                    }

                    Console.WriteLine("\n");
                    // Uncomment this to enable Embedded read memory
                    //embeddedRead(TagProtocol.ISO15693, mulfilter, bRead);

                    // Initialize write memory 
                    byte[] data = new byte[] { 0x11, 0x22, 0x33, 0x44 };
                    WriteMemory writeOp = new WriteMemory(type, address, data);

                    // Execute the tagop
                    _r.ExecuteTagOp(writeOp, mulfilter);
                    // Uncomment this to enable Embedded write data
                    //embeddedRead(TagProtocol.ISO15693, mulfilter, writeOp);

                    //Read memory after block write
                    ReadMemory readOp = new ReadMemory(type, address, length);
                    byte[] readData = (byte[])_r.ExecuteTagOp(readOp, mulfilter);

                    // prints the data read
                    Console.WriteLine("Read Data after performing block write operation: ");
                    foreach (byte i in readData)
                    {
                        Console.Write(" {0:X2}", i);
                    }

                    Console.WriteLine("\n");
#endif

#if ENABLE_M3E_SYSTEM_INFORMATION_MEMORY

                    //Get the system information of tag. Address and length fields have no significance if memory type is BLOCK_SYSTEM_INFORMATION_MEMORY.
                    type = MemoryType.BLOCK_SYSTEM_INFORMATION_MEMORY;
                    address = 0;
                    length = 0;
                    ReadMemory sysInfoOp = new ReadMemory(type, address, length);
                    byte[] systemInfo = (byte[])r.ExecuteTagOp(sysInfoOp, mulfilter);

                    // parsing the system info response
                    if (systemInfo.Length > 0)
                    {
                        parseGetSystemInfoResponse(systemInfo);
                    }
#endif

#if ENABLE_M3E_SECURE_ID
                    // Read secure id of tag. Address and length fields have no significance if memory type is SECURE_ID.
                    type = MemoryType.SECURE_ID;
                    address = 0;
                    length = 0;

                    // Initialize the read memory tagOp
                    ReadMemory secureIdOp = new ReadMemory(type, address, length);

                    // perform embedded tag operation for secureId read as standalone is not supported.
                    embeddedRead(TagProtocol.ISO15693, mulfilter, secureIdOp);

#endif

#if ENABLE_M3E_BLOCK_PROTECTION_STATUS
                    // Get the block protection status of block 0.
                    type = MemoryType.BLOCK_PROTECTION_STATUS_MEMORY;
                    address = 0;
                    length = 1;
                    ReadMemory blkProtectionOp = new ReadMemory(type, address, length);
                    byte[] statusData = (byte[])r.ExecuteTagOp(blkProtectionOp, mulfilter);

                    // parse the block protection status response.
                    if (statusData.Length == length)
                    {
                        parseGetBlockProtectionStatusResponse(statusData, address, length);
                    }
#endif
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

        #region EmbeddedRead
        public static void embeddedRead(TagProtocol protocol, TagFilter filter, TagOp tagop)
        {
            TagReadData[] tagReads = null;
            SimpleReadPlan plan = new SimpleReadPlan(_antennaList, protocol, filter, tagop, 1000);
            _r.ParamSet("/reader/read/plan", plan);
            tagReads = _r.Read(1000);
            // Print tag reads
            foreach (TagReadData tr in tagReads)
            {
                Console.WriteLine(tr.ToString());
                if (tr.isErrorData)
                {
                    // In case of error, show the error to user. Extract error code.
                    int errorCode = ByteConv.ToU16(tr.Data, 0);
                    Console.WriteLine("Embedded Tag operation failed. Error: " + ReaderCodeException.faultCodeToMessage(errorCode));
                }
                else
                {
                    Console.WriteLine("  Data:" + ByteFormat.ToHex(tr.Data, "", " "));
                }
            }
        }
        #endregion

        #region parseGetSystemInfoResponse
        public static void parseGetSystemInfoResponse(byte[] systemInfo)
        {
            int readIndex = 0;
            // Extract 1 byte of Information Flags from response
            byte infoFlags = systemInfo[readIndex++];

            //Extract UID - 8 bytes for Iso15693
            int uidLength = 8;
            byte[] uid = new byte[uidLength];
            Array.Copy(systemInfo, readIndex, uid, 0, uidLength);
            Console.WriteLine("UID: " + ByteFormat.ToHex(uid));
            readIndex += uidLength;
            if (infoFlags == 0)
            {
                Console.WriteLine("No information flags are enabled");
            }
            else
            {
                // Checks Information flags are supported or not and then extracts respective fields information.
                if ((infoFlags & 0x0001) == 0x0001)
                {
                    Console.WriteLine("DSFID is supported and DSFID field is present in the response");
                    //Extract 1 byte of DSFID
                    byte dsfid = systemInfo[readIndex++];
                    Console.WriteLine("DSFID: " + dsfid.ToString());
                }

                if ((infoFlags & 0x0002) == 0x0002)
                {
                    Console.WriteLine("AFI is supported and AFI field is present in the response");
                    //Extract 1 byte of AFI
                    byte afi = systemInfo[readIndex++];
                    Console.WriteLine("AFI: " + afi.ToString());
                }

                if ((infoFlags & 0x0004) == 0x0004)
                {
                    Console.WriteLine("VICC memory size is supported and VICC field is present in the response");
                    //Extract 2 bytes of VICC information
                    UInt16 viccInfo = ByteConv.ToU16(systemInfo, readIndex);
                    byte maxBlockCount = (byte)(viccInfo & 0xFF); // holds the number of blocks
                    Console.WriteLine("Max Block count: " + maxBlockCount);
                    byte blocksize = (byte)((viccInfo & 0x1F00) >> 8); // holds blocksize
                    Console.WriteLine("Block Size: " + blocksize);
                    readIndex += 2;
                }

                if ((infoFlags & 0x0008) == 0x0008)
                {
                    Console.WriteLine("IC reference is supported and IC reference is present in the response");
                    // Extract 1 byte of IC reference
                    byte icRef = systemInfo[readIndex++];
                    Console.WriteLine("IC Reference: " + icRef.ToString());
                }
            }
        }
        #endregion

        #region parseSecureIdResponse
        public static void parseSecureIdResponse(byte[] rsp)
        {
            int readIndex = 0;

            //Extract UID length
            int uidLength = rsp[readIndex];

            // Extract UID based on length
            byte[] uid = new byte[uidLength];

            //Update the read index and copy the uid into uid array.
            readIndex += 1;
            Array.Copy(rsp, readIndex, uid, 0, uidLength);
            Console.WriteLine("UID: " + ByteFormat.ToHex(uid));
            readIndex += uidLength;

            // Extract Secure id length and ID
            int secureIdLen = rsp[readIndex];
            byte[] secureID = new byte[secureIdLen];

            //Update the read index and copy the Secure id into secureID array.
            readIndex += 1;
            Array.Copy(rsp, readIndex, secureID, 0, secureIdLen);
            Console.WriteLine("Secure ID: " + ByteFormat.ToHex(secureID));
            readIndex += secureIdLen;
        }
        #endregion

        #region parseGetBlockProtectionStatusResponse
        public static void parseGetBlockProtectionStatusResponse(byte[] data, UInt32 address, byte length)
        {
            byte lockStatus;
            for (int i = 0; i < length; i++)
            {
                lockStatus = data[i];
                // Block lock status
                if ((lockStatus & 0x01) == 0x01)
                {
                    Console.WriteLine("Block {0} is locked.", address);
                }
                else
                {
                    Console.WriteLine("Block {0} is not locked.", address);
                }
                // Read Password protection status
                if ((lockStatus & 0x02) == 0x02)
                {
                    Console.WriteLine("Read password protection is enabled for block {0}.", address);
                }
                else
                {
                    Console.WriteLine("Read password protection is disabled for block {0}.", address);
                }
                //write password protection status
                if ((lockStatus & 0x04) == 0x04)
                {
                    Console.WriteLine("Write password protection is enabled for block {0}.", address);
                }
                else
                {
                    Console.WriteLine("Write password protection is disabled for block {0}.", address);
                }
                // Page protection lock status
                if ((lockStatus & 0x08) == 0x08)
                {
                    Console.WriteLine("Page protection is locked for block {0}.", address);
                }
                else
                {
                    Console.WriteLine("Page protection is not locked for block {0}", address);
                }

                address++;
            }
        }
        #endregion
    }
}
