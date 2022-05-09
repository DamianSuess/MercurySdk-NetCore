using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;

// Sample program that demonstrates different types and uses of TagFilter objects.
namespace Filter
{
    /// <summary>
    /// Sample program that demonstrates different types and uses of TagFilter objects.
    /// </summary>
    class Filter
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

            // Create Reader object, connecting to physical device
            try
            {
                Reader r;
                TagReadData[] tagReads, filteredTagReads;
                TagFilter filter;

                r = Reader.Create(args[0]);

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
                if (r.isAntDetectEnabled(antennaList))
                {
                    Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                    Usage();
                }

                // In the current system, sequences of Gen2 operations require Session 0,
                // since each operation resingulates the tag.  In other sessions,
                // the tag will still be "asleep" from the preceding singulation.
                Gen2.Session oldSession = (Gen2.Session)r.ParamGet("/reader/gen2/session");
                Gen2.Session newSession = Gen2.Session.S0;
                Console.WriteLine("Changing to Session "+newSession+" (from Session "+oldSession+")");
                r.ParamSet("/reader/gen2/session", newSession);
                Console.WriteLine();

                SimpleReadPlan srp = new SimpleReadPlan(antennaList, TagProtocol.GEN2);
                r.ParamSet("/reader/read/plan", srp);
                //To perform standalone operations
                if (antennaList != null)
                    r.ParamSet("/reader/tagop/antenna", antennaList[0]);

                try
                {
                    Console.WriteLine("Unfiltered Read:");
                    // Read the tags in the field
                    tagReads = r.Read(500);
                    foreach (TagReadData tr in tagReads)
                        Console.WriteLine(tr.ToString());
                    Console.WriteLine();

                    if (0 == tagReads.Length)
                    {
                        Console.WriteLine("No tags found.");
                    }
                    else
                    {
                        // A TagData object may be used as a filter, for example to
                        // perform a tag data operation on a particular tag.
                        Console.WriteLine("Filtered Tagop:");
                        // Read kill password of tag found in previous operation
                        filter = tagReads[0].Tag;
                        Console.WriteLine("Read kill password of tag {0}", filter);
                        Gen2.ReadData tagop = new Gen2.ReadData(Gen2.Bank.RESERVED, 0, 2);
                        try
                        {
                            ushort[] data = (ushort[])r.ExecuteTagOp(tagop, filter);
                            foreach (ushort word in data)
                            {
                                Console.Write("{0:X4}", word);
                            }
                            Console.WriteLine();
                        }
                        catch (ReaderException ex)
                        {
                            Console.WriteLine("Can't read tag: {0}", ex.Message);
                        }
                        Console.WriteLine();


                        // Filter objects that apply to multiple tags are most useful in
                        // narrowing the set of tags that will be read. This is
                        // performed by setting a read plan that contains a filter.

                        // A TagData with a short EPC will filter for tags whose EPC
                        // starts with the same sequence.
                        filter = new TagData(tagReads[0].Tag.EpcString.Substring(0, 4));
                        Console.WriteLine("EPCs that begin with {0}:", filter);
                        r.ParamSet("/reader/read/plan",
                            new SimpleReadPlan(antennaList, TagProtocol.GEN2, filter, 1000));
                        filteredTagReads = r.Read(500);
                        foreach (TagReadData tr in filteredTagReads)
                            Console.WriteLine(tr.ToString());
                        Console.WriteLine();

                        // A filter can also be an full Gen2 Select operation.  For
                        // example, this filter matches all Gen2 tags where bits 8-19 of
                        // the TID are 0x003 (that is, tags manufactured by Alien
                        // Technology).
                        // In case of Network readers, ensure that bitLength is a multiple of 8.
                        Console.WriteLine("Tags with Alien Technology TID");
                        filter = new Gen2.Select(false, Gen2.Bank.TID, 8, 12, new byte[] { 0, 0x30 });
                        r.ParamSet("/reader/read/plan", new SimpleReadPlan(antennaList, TagProtocol.GEN2, filter, 1000));
                        filteredTagReads = r.Read(500);
                        foreach (TagReadData tr in filteredTagReads)
                            Console.WriteLine(tr.ToString());
                        Console.WriteLine();

                        if (r is SerialReader)
                        {
                          // A filter can also be Gen2 Truncate  Select operation. 
                          // Truncate indicates whether a Tag’s backscattered reply shall be truncated to those EPC bits that follow Mask.
                          // For example, truncated select starting with PC word start address and length of 16 bits
                          // In case of Network readers, ensure that bitLength is a multiple of 8.
                          Console.WriteLine("GEN2 Select Truncate Operation");
                          filter = new Gen2.Select(false, Gen2.Bank.GEN2EPCTRUNCATE, 16, 40, new byte[] { 0x30, 0x00, 0xDE, 0xAD, 0xCA });
                          r.ParamSet("/reader/read/plan", new SimpleReadPlan(antennaList, TagProtocol.GEN2, filter, 1000));
                          filteredTagReads = r.Read(500);
                          foreach (TagReadData tr in filteredTagReads)
                            Console.WriteLine(tr.ToString());
                          Console.WriteLine();

                          // A filter can also perform Gen2 Tag Filtering.
                          // Major advantage of this feature is to limit the EPC response to user specified length field and all others will be rejected by firmware.
                          // invert, bitPointer, mask : Parameters will be ignored when TMR_GEN2_EPC_LENGTH_FILTER is used
                          // maskBitLength : Specified EPC Length used for filtering
                          // For example, Tag filtering will be applied on EPC with 128 bits length, rest of the tags will be ignored
                          // In case of Network readers, ensure that bitLength is a multiple of 8.
                          Console.WriteLine("GEN2 Tag Filter Based on EPC Length");
                          filter = new Gen2.Select(false, Gen2.Bank.GEN2EPCLENGTHFILTER, 16, 128, new byte[] { 0x30, 0x00 });
                          r.ParamSet("/reader/read/plan", new SimpleReadPlan(antennaList, TagProtocol.GEN2, filter, 1000));
                          filteredTagReads = r.Read(500);
                          foreach (TagReadData tr in filteredTagReads)
                            Console.WriteLine(tr.ToString());
                          Console.WriteLine();
                        }

                        // Gen2 Select may also be inverted, to give all non-matching tags
                        // In case of Network readers, ensure that bitLength is a multiple of 8.
                        Console.WriteLine("Tags without Alien Technology TID");
                        filter = new Gen2.Select(true, Gen2.Bank.TID, 8, 12, new byte[] { 0, 0x30 });
                        r.ParamSet("/reader/read/plan", new SimpleReadPlan(antennaList, TagProtocol.GEN2, filter, 1000));
                        filteredTagReads = r.Read(500);
                        foreach (TagReadData tr in filteredTagReads)
                            Console.WriteLine(tr.ToString());
                        Console.WriteLine();

                        // Filters can also be used to match tags that have already been
                        // read. This form can only match on the EPC, as that's the only
                        // data from the tag's memory that is contained in a TagData
                        // object.
                        // Note that this filter has invert=true. This filter will match
                        // tags whose bits do not match the selection mask.
                        // Also note the offset - the EPC code starts at bit 32 of the
                        // EPC memory bank, after the StoredCRC and StoredPC.
                        // In case of Network readers, ensure that bitLength is a multiple of 8.
                        filter = new Gen2.Select(true, Gen2.Bank.EPC, 32, 2, new byte[] { (byte)0xC0 });
                        Console.WriteLine("EPCs with first 2 bits equal to zero (post-filtered):");
                        foreach (TagReadData tr in tagReads) // unfiltered tag reads from the first example
                            if (filter.Matches(tr.Tag))
                                Console.WriteLine(tr.ToString());
                        Console.WriteLine();
                        /**
                     * Multi filter creation and initialization. This filter will match those tags whose bits matches the
                     * selection mask of both tidFilter(tags manufactured by Alien Technology) and epcFilter(epc of first
                     * tag read from tagReads[0]). Target and action are the two new parameters of Gen2.Select class whose
                     * default values could be "Gen2.Select.Target.Select" and "Gen2.Select.Action.ON_N_OFF" respectively
                     * if not provided by the user.
                     *
                     * Gen2 Select Action indicates which Select action to take
                     * (See Gen2 spec /Select commands / Tag response to Action parameter)
                     * |-----------------------------------------------------------------------------|
                     * |  Action  |        Tag Matching            |        Tag Not-Matching         |
                     * |----------|--------------------------------|---------------------------------|
                     * |   0x00   |  Assert SL or Inventoried->A   |  Deassert SL or Inventoried->B  |
                     * |   0x01   |  Assert SL or Inventoried->A   |  Do nothing                     |
                     * |   0x02   |  Do nothing                    |  Deassert SL or Inventoried->B  |
                     * |   0x03   |  Negate SL or (A->B,B->A)      |  Do nothing                     |
                     * |   0x04   |  Deassert SL or Inventoried->B |  Assert SL or Inventoried->A    |
                     * |   0x05   |  Deassert SL or Inventoried->B |  Do nothing                     |
                     * |   0x06   |  Do nothing                    |  Assert SL or Inventoried->A    |
                     * |   0x07   |  Do nothing                    |  Negate SL or (A->B,B->A)       |
                     *
                     *
                     * To improve readability and ease typing, these names abbreviate the official terminology of the Gen2 spec.
                     * <A>_N_<B>: The "_N_" stands for "Non-Matching".
                     * The <A> clause before the _N_ describes what happens to Matching tags.
                     * The <B> clause after the _N_ describes what happens to Non-Matching tags.
                     * (Alternately, you can pronounce "_N_" as "and", or "&"; i.e.,
                     * the pair of Matching / Non-Matching actions is known as "<A> and <B>".)
                     *
                     * ON: assert SL or inventoried -> A
                     * OFF: deassert SL or inventoried -> B
                     * NEG: negate SL or (A->B, B->A)
                     * NOP: do nothing
                     *
                     * The enum is simply a transliteration of the Gen2 spec's table: "Tag response to Action parameter"
                     */

                        // create and initialize tidFilter
                        // In case of Network readers, ensure that bitLength is a multiple of 8.
                        Gen2.Select tidFilter = new Gen2.Select(false, Gen2.Bank.TID, 32, 16, new byte[] { (byte)0x01, (byte)0x2E });
                        tidFilter.target = Gen2.Select.Target.Select;
                        tidFilter.action = Gen2.Select.Action.ON_N_OFF;

                        // create and initialize epcFilter
                        // In case of Network readers, ensure that bitLength is a multiple of 8.
                        Gen2.Select epcFilter = new Gen2.Select(false, Gen2.Bank.EPC, 32, 16, new byte[] { (byte)0x11, (byte)0x22 });
                        epcFilter.target = Gen2.Select.Target.Select;
                        epcFilter.action = Gen2.Select.Action.ON_N_OFF;

                        // Initialize multifilter with tagFilter array containing list of filters
                        // In case of Network readers, ensure that bitLength is a multiple of 8.
                        MultiFilter multiFilter = new MultiFilter(new TagFilter[] { tidFilter, epcFilter });
                        r.ParamSet("/reader/read/plan", new SimpleReadPlan(antennaList, TagProtocol.GEN2, multiFilter, 1000));
                        Console.WriteLine("Reading tags which matches multi filter criteria \n", filter.ToString());
                        filteredTagReads = r.Read(500);
                        foreach (TagReadData tr in filteredTagReads)
                        {
                            Console.WriteLine(tr.ToString());
                        }
                        Console.WriteLine();
                    }
                }
                finally
                {
                    // Restore original settings
                    Console.WriteLine("Restoring Session " + oldSession);
                    r.ParamSet("/reader/gen2/session", oldSession);
                }

                // Shut down reader
                r.Destroy();
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
