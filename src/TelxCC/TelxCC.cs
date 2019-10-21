/*!
(c) 2011-2014 Forers, s. r. o.: telxcc

telxcc conforms to ETSI 300 706 Presentation Level 1.5: Presentation Level 1 defines the basic Teletext page,
characterised by the use of spacing attributes only and a limited alphanumeric and mosaics repertoire.
Presentation Level 1.5 decoder responds as Level 1 but the character repertoire is extended via packets X/26.
Selection of national option sub-sets related features from Presentation Level 2.5 feature set have been implemented, too.
(X/28/0 Format 1, X/28/4, M/29/0 and M/29/4 packets)

Further documentation:
ETSI TS 101 154 V1.9.1 (2009-09), Technical Specification
  Digital Video Broadcasting (DVB); Specification for the use of Video and Audio Coding in Broadcasting Applications based on the MPEG-2 Transport Stream
ETSI EN 300 231 V1.3.1 (2003-04), European Standard (Telecommunications series)
  Television systems; Specification of the domestic video Programme Delivery Control system (PDC)
ETSI EN 300 472 V1.3.1 (2003-05), European Standard (Telecommunications series)
  Digital Video Broadcasting (DVB); Specification for conveying ITU-R System B Teletext in DVB bitstreams
ETSI EN 301 775 V1.2.1 (2003-05), European Standard (Telecommunications series)
  Digital Video Broadcasting (DVB); Specification for the carriage of Vertical Blanking Information (VBI) data in DVB bitstreams
ETS 300 706 (May 1997)
  Enhanced Teletext Specification
ETS 300 708 (March 1997)
  Television systems; Data transmission within Teletext
ISO/IEC STANDARD 13818-1 Second edition (2000-12-01)
  Information technology — Generic coding of moving pictures and associated audio information: Systems
ISO/IEC STANDARD 6937 Third edition (2001-12-15)
  Information technology — Coded graphic character set for text communication — Latin alphabet
Werner Brückner -- Teletext in digital television
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TelxCCSharp
{
    public class TelxCC
    {
        const string TelxccVersion = "2.6.0";

        private const int ExitSuccess = 0;
        private const int ExitFailure = 1;

        private const int SyncByte = 0x47;

        public enum BoolT
        {
            No = 0x00,
            Yes = 0x01,
            Undef = 0xff
        }

        // size of a (M2)TS packet in bytes (TS = 188, M2TS = 192)
        private const int TsPacketSize = 192;

        // size of a TS packet payload in bytes
        private const int TsPacketPayloadSize = 184;

        // size of a packet payload buffer
        private const int PayloadBufferSize = 4096;

        public class TsPacket
        {
            public int Sync { get; set; }
            public int TransportError { get; set; }
            public int PayloadUnitStart { get; set; }
            public int TransportPriority { get; set; }
            public int Pid { get; set; }
            public int ScramblingControl { get; set; }
            public int AdaptationFieldExists { get; set; }
            public int ContinuityCounter { get; set; }
        }

        public class PatSection
        {
            public int ProgramNum { get; set; }
            public int ProgramPid { get; set; }
        }

        public class Pat
        {
            public int PointerField { get; set; }
            public int TableId { get; set; }
            public int SectionLength { get; set; }
            public int CurrentNextIndicator { get; set; }
        }

        public class PmtProgramDescriptor
        {
            public int StreamType { get; set; }
            public int ElementaryPid { get; set; }
            public int EsInfoLength { get; set; }
        }

        public class Pmt
        {
            public int PointerField { get; set; }
            public int TableId { get; set; }
            public int SectionLength { get; set; }
            public int ProgramNum { get; set; }
            public int CurrentNextIndicator { get; set; }
            public int PcrPid { get; set; }
            public int ProgramInfoLength { get; set; }
        }

        public enum DataUnitT
        {
            DataUnitEbuTeletextNonSubtitle = 0x02,
            DataUnitEbuTeletextSubtitle = 0x03,
            DataUnitEbuTeletextInverted = 0x0c,
            DataUnitVps = 0xc3,
            DataUnitClosedCaptions = 0xc5
        }

        public enum TransmissionMode
        {
            TransmissionModeParallel = 0,
            TransmissionModeSerial = 1
        }

        private static readonly string[] TtxtColours = 
        {
            //black,   red,       green,     yellow,    blue,      magenta,   cyan,      white
            "#000000", "#ff0000", "#00ff00", "#ffff00", "#0000ff", "#ff00ff", "#00ffff", "#ffffff"
        };

        public class TeletextPacketPayload
        {
            public int ClockIn { get; }
            public int FramingCode { get; }
            public byte[] Address { get; } = new byte[2];
            public byte[] Data { get; } = new byte[40];

            public TeletextPacketPayload(byte[] buffer, int index)
            {
                ClockIn = buffer[index];
                FramingCode = buffer[index + 1];
                Address[0] = buffer[index + 2];
                Address[1] = buffer[index + 3];
                Buffer.BlockCopy(buffer, index + 4, Data, 0, Data.Length);
            }
        }

        public class TeletextPage
        {
            public ulong ShowTimestamp { get; set; }
            public ulong HideTimestamp { get; set; }
            public int[,] Text { get; set; } = new int[25, 40];
            public bool Tainted { get; set; }
        }

        // application config global variable
        public class Config
        {
            public string InputName { get; set; }
            public string OutputName { get; set; }
            public bool Verbose { get; set; } // should telxcc be verbose?
            public int Page { get; set; } // teletext page containing cc we want to filter
            public int Tid { get; set; } // 13-bit packet ID for teletext stream
            public double Offset { get; set; } // time offset in seconds
            public bool Colours { get; set; } // output <font...></font> tags
            public bool Bom { get; set; } // print UTF-8 BOM characters at the beginning of output
            public bool NonEmpty { get; set; } // produce at least one (dummy) frame
            public ulong UtcRefValue { get; set; } // UTC referential value
            public bool SeMode { get; set; } // FIXME: move SE_MODE to output module
            public bool M2Ts { get; set; } // consider input stream is af s M2TS, instead of TS

            public Config()
            {
                InputName = null;
                OutputName = null;
                Verbose = false;
                Page = 0;
                Tid = 0;
                Offset = 0;
                Colours = false;
                Bom = true;
                NonEmpty = false;
                UtcRefValue = 0;
                SeMode = false;
                M2Ts = false;
            }
        }

        private static readonly Config config = new Config();

        private static Stream _fin;
        private static readonly StringBuilder Fout = new StringBuilder();

        // application states -- flags for notices that should be printed only once
        public class States
        {
            public bool ProgrammeInfoProcessed { get; set; }
            public bool PtsInitialized { get; set; }
        }
        private static readonly States states = new States();

        // SRT frames produced
        private static int _framesProduced;

        // subtitle type pages bitmap, 2048 bits = 2048 possible pages in teletext (excl. subpages)
        private static readonly byte[] CcMap = new byte[256];

        // global TS PCR value
        private static ulong _globalTimestamp;

        // last timestamp computed
        private static ulong _lastTimestamp;

        // working teletext page buffer
        private static readonly TeletextPage PageBuffer = new TeletextPage();

        // teletext transmission mode
        private static TransmissionMode _transmissionMode = TransmissionMode.TransmissionModeSerial;

        // flag indicating if incoming data should be processed or ignored
        private static bool _receivingData;

        // current charset (charset can be -- and always is -- changed during transmission)
        public class PrimaryCharset
        {
            public int Current { get; set; }
            public int G0M29 { get; set; }
            public int G0X28 { get; set; }

            public PrimaryCharset()
            {
                Current = 0x00;
                G0M29 = (int)BoolT.Undef;
                G0X28 = (int)BoolT.Undef;
            }
        }
        private static readonly PrimaryCharset primaryCharset = new PrimaryCharset();

        // entities, used in colour mode, to replace unsafe HTML tag chars
        private static readonly Dictionary<char, string> Entities = new Dictionary<char, string>
        {
            { '<', "&lt;" },
            { '>', "&gt;" },
            { '&', "&amp;" }
        };

        // PMTs table
        private const int TsPmtMapSize = 128;
        private static readonly int[] PmtMap = new int[TsPmtMapSize];
        private static int _pmtMapCount;

        // TTXT streams table
        private const int TsPmtTtxtMapSize = 128;
        private static readonly int[] PmtTtxtMap = new int[TsPmtMapSize];
        private static int _pmtTtxtMapCount;

        // helper, linear searcher for a value
        private static bool InArray(int[] array, int length, int element)
        {
            for (var i = 0; i < length; i++)
            {
                if (array[i] == element)
                {
                    return true;
                }
            }
            return false;
        }

        // extracts magazine number from teletext page
        private static int Magazine(int p)
        {
            return (p >> 8) & 0xf;
        }

        // extracts page number from teletext page
        private static int Page(int p)
        {
            return p & 0xff;
        }

        // ETS 300 706, chapter 8.2
        private static byte Unham84(byte a)
        {
            var r = Hamming.Unham84[a];
            if (r == 0xff)
            {
                r = 0;
                if (config.Verbose)
                {
                    Console.WriteLine($"! Unrecoverable data error; UNHAM8/4({a:X2})");
                }
            }
            return (byte)(r & 0x0f);
        }

        // ETS 300 706, chapter 8.3
        private static uint Unham2418(int a)
        {
            int test = 0;

            // Tests A-F correspond to bits 0-6 respectively in 'test'.
            for (int i = 0; i < 23; i++)
                test ^= ((a >> i) & 0x01) * (i + 33);
            // Only parity bit is tested for bit 24
            test ^= ((a >> 23) & 0x01) * 32;

            if ((test & 0x1f) != 0x1f)
            {
                // Not all tests A-E correct
                if ((test & 0x20) == 0x20)
                {
                    // F correct: Double error
                    return 0xffffffff;
                }
                // Test F incorrect: Single error
                a ^= 1 << (30 - test);
            }
            var result = (a & 0x000004) >> 2 | (a & 0x000070) >> 3 | (a & 0x007f00) >> 4 | (a & 0x7f0000) >> 5;
            return (uint)result;
        }

        private static void RemapG0Charset(int c)
        {
            if (c != primaryCharset.Current)
            {
                var m = Tables.G0LatinNationalSubsetsMap[c];
                if (m == 0xff)
                {
                    Console.WriteLine($"- G0 Latin National Subset ID {(c >> 3):X2}.{(c & 0x7):X2} is not implemented");
                }
                else
                {
                    for (int j = 0; j < 13; j++) Tables.G0[(int)Tables.G0CharsetsT.Latin, Tables.G0LatinNationalSubsetsPositions[j]] = Tables.G0LatinNationalSubsets[m].Characters[j];
                    if (config.Verbose) Console.WriteLine($"- Using G0 Latin National Subset ID {c >> 3:X2}.{c & 0x7:X2} ({Tables.G0LatinNationalSubsets[m].Language})");
                    primaryCharset.Current = c;
                }
            }
        }

        private static string TimestampToSrtTime(ulong timestamp)
        {
            var p = timestamp;
            var h = p / 3600000;
            var m = p / 60000 - 60 * h;
            var s = p / 1000 - 3600 * h - 60 * m;
            var u = p - 3600000 * h - 60000 * m - 1000 * s;
            return $"{h:00}:{m:00}:{s:00},{u:000}";
        }

        // UCS-2 (16 bits) to UTF-8 (Unicode Normalization Form C (NFC)) conversion
        private static string Ucs2ToUtf8(int ch)
        {
            var r = new byte[4];
            if (ch < 0x80)
            {
                r[0] = (byte)(ch & 0x7f);
                return Encoding.UTF8.GetString(r, 0, 1);
            }

            if (ch < 0x800)
            {
                r[0] = (byte)((ch >> 6) | 0xc0);
                r[1] = (byte)((ch & 0x3f) | 0x80);
                return Encoding.UTF8.GetString(r, 0, 2);
            }

            r[0] = (byte)((ch >> 12) | 0xe0);
            r[1] = (byte)(((ch >> 6) & 0x3f) | 0x80);
            r[2] = (byte)((ch & 0x3f) | 0x80);
            return Encoding.UTF8.GetString(r, 0, 3);
        }

        // check parity and translate any reasonable teletext character into ucs2
        private static int TelxToUcs2(byte c)
        {
            if (Hamming.Parity8[c] == 0)
            {
                if (config.Verbose) Console.WriteLine($"! Unrecoverable data error; PARITY({c:X2})");
                return 0x20;
            }

            var r = c & 0x7f;
            if (r >= 0x20) r = Tables.G0[(int)Tables.G0CharsetsT.Latin, r - 0x20];
            return r;
        }

        // FIXME: implement output modules (to support different formats, printf formatting etc)
        static void ProcessPage(TeletextPage page)
        {
            //#if DEBUG
            //            for (int row = 1; row < 25; row++)
            //            {
            //                fout.Append($"# DEBUG[{row}]: ");
            //                for (int col = 0; col < 40; col++) fout.Append($"{(page.text[row, col]):X2} ");
            //                fout.AppendLine();
            //            }
            //            fout.AppendLine();
            //#endif

            // optimization: slicing column by column -- higher probability we could find boxed area start mark sooner
            bool pageIsEmpty = true;
            for (var col = 0; col < 40; col++)
            {
                for (var row = 1; row < 25; row++)
                {
                    if (page.Text[row, col] == 0x0b)
                    {
                        pageIsEmpty = false;
                        goto page_is_empty;
                    }
                }
            }
            page_is_empty:
            if (pageIsEmpty) return;

            if (page.ShowTimestamp > page.HideTimestamp) page.HideTimestamp = page.ShowTimestamp;

            if (config.SeMode)
            {
                ++_framesProduced;
                Fout.Append($"{(double)page.ShowTimestamp / 1000.0}|");
            }
            else
            {
                var timeCodeShow = TimestampToSrtTime(page.ShowTimestamp);
                var timeCodeHide = TimestampToSrtTime(page.HideTimestamp);
                Fout.AppendLine($"{++_framesProduced}{Environment.NewLine}{timeCodeShow} --> {timeCodeHide}");
            }

            // process data
            for (var row = 1; row < 25; row++)
            {
                // anchors for string trimming purpose
                var colStart = 40;
                var colStop = 40;

                for (var col = 39; col >= 0; col--)
                {
                    if (page.Text[row, col] == 0xb)
                    {
                        colStart = col;
                        break;
                    }
                }
                // line is empty
                if (colStart > 39) continue;

                for (var col = colStart + 1; col <= 39; col++)
                {
                    if (page.Text[row, col] > 0x20)
                    {
                        if (colStop > 39) colStart = col;
                        colStop = col;
                    }
                    if (page.Text[row, col] == 0xa) break;
                }
                // line is empty
                if (colStop > 39) continue;

                // ETS 300 706, chapter 12.2: Alpha White ("Set-After") - Start-of-row default condition.
                // used for colour changes _before_ start box mark
                // white is default as stated in ETS 300 706, chapter 12.2
                // black(0), red(1), green(2), yellow(3), blue(4), magenta(5), cyan(6), white(7)
                var foregroundColor = 0x7;
                bool fontTagOpened = false;

                for (var col = 0; col <= colStop; col++)
                {
                    // v is just a shortcut
                    var v = page.Text[row, col];

                    if (col < colStart)
                    {
                        if (v <= 0x7) foregroundColor = v;
                    }

                    if (col == colStart)
                    {
                        if ((foregroundColor != 0x7) && (config.Colours))
                        {
                            Fout.Append($"<font color=\"{TtxtColours[foregroundColor]}\">");
                            fontTagOpened = true;
                        }
                    }

                    if (col >= colStart)
                    {
                        if (v <= 0x7)
                        {
                            // ETS 300 706, chapter 12.2: Unless operating in "Hold Mosaics" mode,
                            // each character space occupied by a spacing attribute is displayed as a SPACE.
                            if (config.Colours)
                            {
                                if (fontTagOpened)
                                {
                                    Fout.Append("</font> ");
                                    fontTagOpened = false;
                                }

                                // black is considered as white for telxcc purpose
                                // telxcc writes <font/> tags only when needed
                                if (v > 0x0 && v < 0x7)
                                {
                                    Fout.Append($"<font color=\"{TtxtColours[v]}\">");
                                    fontTagOpened = true;
                                }
                            }
                            else v = 0x20;
                        }

                        if (v >= 0x20)
                        {
                            // translate some chars into entities, if in colour mode
                            if (config.Colours)
                            {
                                if (Entities.ContainsKey(Convert.ToChar(v)))
                                {
                                    Fout.Append(Entities[Convert.ToChar(v)]);
                                    // v < 0x20 won't be printed in next block
                                    v = 0;
                                    break;
                                }
                            }
                        }

                        if (v >= 0x20)
                        {
                            Fout.Append(Ucs2ToUtf8(v));
                        }
                    }
                }

                // no tag will left opened!
                if ((config.Colours) && (fontTagOpened))
                {
                    Fout.Append("</font>");
                    fontTagOpened = false;
                }

                // line delimiter
                Fout.Append(config.SeMode ? " " : Environment.NewLine);
            }
            Fout.AppendLine();
        }

        private static void ProcessTelxPacket(DataUnitT dataUnitId, TeletextPacketPayload packet, ulong timestamp)
        {
            // variable names conform to ETS 300 706, chapter 7.1.2
            var address = (Unham84(packet.Address[1]) << 4) | Unham84(packet.Address[0]);
            var m = address & 0x7;
            if (m == 0) m = 8;
            var y = (address >> 3) & 0x1f;
            var designationCode = y > 25 ? Unham84(packet.Data[0]) : 0x00;

            if (y == 0)
            {
                // CC map
                var i = (Unham84(packet.Data[1]) << 4) | Unham84(packet.Data[0]);
                var flagSubtitle = (Unham84(packet.Data[5]) & 0x08) >> 3;
                CcMap[i] |= (byte)(flagSubtitle << (m - 1));

                if (config.Page == 0 && flagSubtitle == (int)BoolT.Yes && i < 0xff)
                {
                    config.Page = (m << 8) | (Unham84(packet.Data[1]) << 4) | Unham84(packet.Data[0]);
                    Console.WriteLine($"- No teletext page specified, first received suitable page is {config.Page}, not guaranteed");
                }

                // Page number and control bits
                var pageNumber = (m << 8) | (Unham84(packet.Data[1]) << 4) | Unham84(packet.Data[0]);
                var charset = ((Unham84(packet.Data[7]) & 0x08) | (Unham84(packet.Data[7]) & 0x04) | (Unham84(packet.Data[7]) & 0x02)) >> 1;
                //uint8_t flag_suppress_header = unham_8_4(packet.data[6]) & 0x01;
                //uint8_t flag_inhibit_display = (unham_8_4(packet.data[6]) & 0x08) >> 3;

                // ETS 300 706, chapter 9.3.1.3:
                // When set to '1' the service is designated to be in Serial mode and the transmission of a page is terminated
                // by the next page header with a different page number.
                // When set to '0' the service is designated to be in Parallel mode and the transmission of a page is terminated
                // by the next page header with a different page number but the same magazine number.
                // The same setting shall be used for all page headers in the service.
                // ETS 300 706, chapter 7.2.1: Page is terminated by and excludes the next page header packet
                // having the same magazine address in parallel transmission mode, or any magazine address in serial transmission mode.
                _transmissionMode = (TransmissionMode)(Unham84(packet.Data[7]) & 0x01);

                // FIXME: Well, this is not ETS 300 706 kosher, however we are interested in DATA_UNIT_EBU_TELETEXT_SUBTITLE only
                if (_transmissionMode == TransmissionMode.TransmissionModeParallel && (dataUnitId != DataUnitT.DataUnitEbuTeletextSubtitle)) return;

                if (_receivingData && (
                        _transmissionMode == TransmissionMode.TransmissionModeSerial && (Page(pageNumber) != Page(config.Page)) ||
                        _transmissionMode == TransmissionMode.TransmissionModeParallel && (Page(pageNumber) != Page(config.Page)) && (m == Magazine(config.Page))
                    ))
                {
                    _receivingData = false;
                    return;
                }

                // Page transmission is terminated, however now we are waiting for our new page
                if (pageNumber != config.Page) return;

                // Now we have the beginning of page transmission; if there is page_buffer pending, process it
                if (PageBuffer.Tainted)
                {
                    // it would be nice, if subtitle hides on previous video frame, so we contract 40 ms (1 frame @25 fps)
                    PageBuffer.HideTimestamp = timestamp - 40;
                    ProcessPage(PageBuffer);
                }

                PageBuffer.ShowTimestamp = timestamp;
                PageBuffer.HideTimestamp = 0;
                PageBuffer.Text = new int[25, 40]; //memset(page_buffer.text, 0x00, sizeof(page_buffer.text));
                PageBuffer.Tainted = false;
                _receivingData = true;
                primaryCharset.G0X28 = (int)BoolT.Undef;

                var c = primaryCharset.G0M29 != (int)BoolT.Undef ? primaryCharset.G0M29 : charset;
                RemapG0Charset(c);

                /*
                // I know -- not needed; in subtitles we will never need disturbing teletext page status bar
                // displaying tv station name, current time etc.
                if (flag_suppress_header == NO) {
                    for (uint8_t i = 14; i < 40; i++) page_buffer.text[y,i] = telx_to_ucs2(packet.data[i]);
                    //page_buffer.tainted = YES;
                }
                */
            }
            else if (m == Magazine(config.Page) && y >= 1 && y <= 23 && _receivingData)
            {
                // ETS 300 706, chapter 9.4.1: Packets X/26 at presentation Levels 1.5, 2.5, 3.5 are used for addressing
                // a character location and overwriting the existing character defined on the Level 1 page
                // ETS 300 706, annex B.2.2: Packets with Y = 26 shall be transmitted before any packets with Y = 1 to Y = 25;
                // so page_buffer.text[y,i] may already contain any character received
                // in frame number 26, skip original G0 character
                for (var i = 0; i < 40; i++) if (PageBuffer.Text[y, i] == 0x00) PageBuffer.Text[y, i] = TelxToUcs2(packet.Data[i]);
                PageBuffer.Tainted = true;
            }
            else if (m == Magazine(config.Page) && y == 26 && _receivingData)
            {
                // ETS 300 706, chapter 12.3.2: X/26 definition
                var x26Row = 0;
                var x26Col = 0;

                var triplets = new uint[13];
                var j = 0;
                for (var i = 1; i < 40; i += 3, j++) triplets[j] = Unham2418((packet.Data[i + 2] << 16) | (packet.Data[i + 1] << 8) | packet.Data[i]);

                for (var j2 = 0; j2 < 13; j2++)
                {
                    if (triplets[j2] == 0xffffffff)
                    {
                        // invalid data (HAM24/18 uncorrectable error detected), skip group
                        if (config.Verbose) Console.WriteLine($"! Unrecoverable data error; UNHAM24/18()={triplets[j2]}");
                        continue;
                    }

                    var data = (triplets[j2] & 0x3f800) >> 11;
                    var mode = (triplets[j2] & 0x7c0) >> 6;
                    var address2 = triplets[j2] & 0x3f;
                    var rowAddressGroup = (address2 >= 40) && (address2 <= 63);

                    // ETS 300 706, chapter 12.3.1, table 27: set active position
                    if (mode == 0x04 && rowAddressGroup)
                    {
                        x26Row = (int)(address2 - 40);
                        if (x26Row == 0) x26Row = 24;
                        x26Col = 0;
                    }

                    // ETS 300 706, chapter 12.3.1, table 27: termination marker
                    if (mode >= 0x11 && mode <= 0x1f && rowAddressGroup) break;

                    // ETS 300 706, chapter 12.3.1, table 27: character from G2 set
                    if (mode == 0x0f && !rowAddressGroup)
                    {
                        x26Col = (int)address2;
                        if (data > 31) PageBuffer.Text[x26Row, x26Col] = Tables.G2[0, data - 0x20];
                    }

                    // ETS 300 706, chapter 12.3.1, table 27: G0 character with diacritical mark
                    if (mode >= 0x11 && mode <= 0x1f && !rowAddressGroup)
                    {
                        x26Col = (int)address2;

                        // A - Z
                        if (data >= 65 && data <= 90) PageBuffer.Text[x26Row, x26Col] = Tables.G2Accents[mode - 0x11, data - 65];
                        // a - z
                        else if (data >= 97 && data <= 122) PageBuffer.Text[x26Row, x26Col] = Tables.G2Accents[mode - 0x11, data - 71];
                        // other
                        else PageBuffer.Text[x26Row, x26Col] = TelxToUcs2((byte)data);
                    }
                }
            }
            else if (m == Magazine(config.Page) && y == 28 && _receivingData)
            {
                // TODO:
                //   ETS 300 706, chapter 9.4.7: Packet X/28/4
                //   Where packets 28/0 and 28/4 are both transmitted as part of a page, packet 28/0 takes precedence over 28/4 for all but the colour map entry coding.
                if (designationCode == 0 || designationCode == 4)
                {
                    // ETS 300 706, chapter 9.4.2: Packet X/28/0 Format 1
                    // ETS 300 706, chapter 9.4.7: Packet X/28/4
                    uint triplet0 = Unham2418((packet.Data[3] << 16) | (packet.Data[2] << 8) | packet.Data[1]);

                    if (triplet0 == 0xffffffff)
                    {
                        // invalid data (HAM24/18 uncorrectable error detected), skip group
                        if (config.Verbose) Console.WriteLine($"! Unrecoverable data error; UNHAM24/18()={triplet0}");
                    }
                    else
                    {
                        // ETS 300 706, chapter 9.4.2: Packet X/28/0 Format 1 only
                        if ((triplet0 & 0x0f) == 0x00)
                        {
                            primaryCharset.G0X28 = (int)((triplet0 & 0x3f80) >> 7);
                            RemapG0Charset(primaryCharset.G0X28);
                        }
                    }
                }
            }
            else if (m == Magazine(config.Page) && y == 29)
            {
                // TODO:
                //   ETS 300 706, chapter 9.5.1 Packet M/29/0
                //   Where M/29/0 and M/29/4 are transmitted for the same magazine, M/29/0 takes precedence over M/29/4.
                if (designationCode == 0 || designationCode == 4)
                {
                    // ETS 300 706, chapter 9.5.1: Packet M/29/0
                    // ETS 300 706, chapter 9.5.3: Packet M/29/4
                    uint triplet0 = Unham2418((packet.Data[3] << 16) | (packet.Data[2] << 8) | packet.Data[1]);

                    if (triplet0 == 0xffffffff)
                    {
                        // invalid data (HAM24/18 uncorrectable error detected), skip group
                        if (config.Verbose) Console.WriteLine($"! Unrecoverable data error; UNHAM24/18()={triplet0}");
                    }
                    else
                    {
                        // ETS 300 706, table 11: Coding of Packet M/29/0
                        // ETS 300 706, table 13: Coding of Packet M/29/4
                        if ((triplet0 & 0xff) == 0x00)
                        {
                            primaryCharset.G0M29 = (int)((triplet0 & 0x3f80) >> 7);
                            // X/28 takes precedence over M/29
                            if (primaryCharset.G0X28 == (int)BoolT.Undef)
                            {
                                RemapG0Charset(primaryCharset.G0M29);
                            }
                        }
                    }
                }
            }
            else if (m == 8 && y == 30)
            {
                // ETS 300 706, chapter 9.8: Broadcast Service Data Packets
                if (!states.ProgrammeInfoProcessed)
                {
                    // ETS 300 706, chapter 9.8.1: Packet 8/30 Format 1
                    if (Unham84(packet.Data[0]) < 2)
                    {
                        Console.Write("- Programme Identification Data = ");
                        for (var i = 20; i < 40; i++)
                        {
                            var c = TelxToUcs2(packet.Data[i]);
                            // strip any control codes from PID, eg. TVP station
                            if (c < 0x20) continue;

                            Console.Write(Ucs2ToUtf8(c));
                        }
                        Console.WriteLine();

                        // OMG! ETS 300 706 stores timestamp in 7 bytes in Modified Julian Day in BCD format + HH:MM:SS in BCD format
                        // + timezone as 5-bit count of half-hours from GMT with 1-bit sign
                        // In addition all decimals are incremented by 1 before transmission.
                        long t = 0;
                        // 1st step: BCD to Modified Julian Day
                        t += (packet.Data[10] & 0x0f) * 10000;
                        t += ((packet.Data[11] & 0xf0) >> 4) * 1000;
                        t += (packet.Data[11] & 0x0f) * 100;
                        t += ((packet.Data[12] & 0xf0) >> 4) * 10;
                        t += (packet.Data[12] & 0x0f);
                        t -= 11111;
                        // 2nd step: conversion Modified Julian Day to unix timestamp
                        t = (t - 40587) * 86400;
                        // 3rd step: add time
                        t += 3600 * (((packet.Data[13] & 0xf0) >> 4) * 10 + (packet.Data[13] & 0x0f));
                        t += 60 * (((packet.Data[14] & 0xf0) >> 4) * 10 + (packet.Data[14] & 0x0f));
                        t += (((packet.Data[15] & 0xf0) >> 4) * 10 + (packet.Data[15] & 0x0f));
                        t -= 40271;
                        // 4th step: conversion to time_t
                        var span = TimeSpan.FromTicks(t * TimeSpan.TicksPerSecond);
                        var t2 = new DateTime(1970, 1, 1).Add(span);
                        var localTime = TimeZone.CurrentTimeZone.ToLocalTime(t2); // TimeZone.CurrentTimeZone.ToUniversalTime(t2); ?

                        Console.WriteLine($"- Programme Timestamp (UTC) = {localTime.ToLongDateString()} {localTime.ToLongTimeString()}");

                        if (config.Verbose) Console.WriteLine($"- Transmission mode = {(_transmissionMode == TransmissionMode.TransmissionModeSerial ? "serial" : "parallel")}");

                        if (config.SeMode)
                        {
                            Console.WriteLine($"- Broadcast Service Data Packet received, resetting UTC referential value to {t} seconds");
                            config.UtcRefValue = (ulong)t;
                            states.PtsInitialized = false;
                        }

                        states.ProgrammeInfoProcessed = true;
                    }
                }
            }
        }

        private static BoolT _usingPts = BoolT.Undef;
        private static long _delta;
        private static long _t0;

        private static void ProcessPesPacket(byte[] buffer, int size)
        {
            if (size < 6) return;

            // Packetized Elementary Stream (PES) 32-bit start code
            ulong pesPrefix = (ulong)((buffer[0] << 16) | (buffer[1] << 8) | buffer[2]);
            var pesStreamId = buffer[3];

            // check for PES header
            if (pesPrefix != 0x000001) return;

            // stream_id is not "Private Stream 1" (0xbd)
            if (pesStreamId != 0xbd) return;

            // PES packet length
            // ETSI EN 301 775 V1.2.1 (2003-05) chapter 4.3: (N x 184) - 6 + 6 B header
            var pesPacketLength = 6 + ((buffer[4] << 8) | buffer[5]);
            // Can be zero. If the "PES packet length" is set to zero, the PES packet can be of any length.
            // A value of zero for the PES packet length can be used only when the PES packet payload is a video elementary stream.
            if (pesPacketLength == 6) return;

            // truncate incomplete PES packets
            if (pesPacketLength > size) pesPacketLength = size;

            bool optionalPesHeaderIncluded = false;
            var optionalPesHeaderLength = 0;
            // optional PES header marker bits (10.. ....)
            if ((buffer[6] & 0xc0) == 0x80)
            {
                optionalPesHeaderIncluded = true;
                optionalPesHeaderLength = buffer[8];
            }

            // should we use PTS or PCR?
            if (_usingPts == BoolT.Undef)
            {
                if (optionalPesHeaderIncluded && (buffer[7] & 0x80) > 0)
                {
                    _usingPts = BoolT.Yes;
                    if (config.Verbose) Console.WriteLine("- PID 0xbd PTS available");
                }
                else
                {
                    _usingPts = BoolT.No;
                    if (config.Verbose) Console.WriteLine(" - PID 0xbd PTS unavailable, using TS PCR");
                }
            }

            ulong t;
            // If there is no PTS available, use global PCR
            if (_usingPts == BoolT.No)
            {
                t = _globalTimestamp;
            }
            else
            {
                // PTS is 33 bits wide, however, timestamp in ms fits into 32 bits nicely (PTS/90)
                // presentation and decoder timestamps use the 90 KHz clock, hence PTS/90 = [ms]
                // __MUST__ assign value to uint64_t and __THEN__ rotate left by 29 bits
                // << is defined for signed int (as in "C" spec.) and overflow occures
                long pts = buffer[9] & 0x0e;
                pts <<= 29;
                pts |= buffer[10] << 22;
                pts |= (buffer[11] & 0xfe) << 14;
                pts |= buffer[12] << 7;
                pts |= (buffer[13] & 0xfe) >> 1;
                t = (ulong)pts / 90;
            }

            if (!states.PtsInitialized)
            {
                _delta = (long)(1000 * config.Offset + 1000 * config.UtcRefValue - t);
                states.PtsInitialized = true;

                if (_usingPts == BoolT.No && _globalTimestamp == 0)
                {
                    // We are using global PCR, nevertheless we still have not received valid PCR timestamp yet
                    states.PtsInitialized = false;
                }
            }
            if (t < (ulong)_t0) _delta = (long)_lastTimestamp;
            _lastTimestamp = t + (ulong)_delta;
            _t0 = (long)t;

            // skip optional PES header and process each 46 bytes long teletext packet
            var i = 7;
            if (optionalPesHeaderIncluded) i += 3 + optionalPesHeaderLength;
            while (i <= pesPacketLength - 6)
            {
                var dataUnitId = buffer[i++];
                var dataUnitLen = buffer[i++];

                if (dataUnitId == (int)DataUnitT.DataUnitEbuTeletextNonSubtitle || dataUnitId == (int)DataUnitT.DataUnitEbuTeletextSubtitle)
                {
                    // teletext payload has always size 44 bytes
                    if (dataUnitLen == 44)
                    {
                        // reverse endianess (via lookup table), ETS 300 706, chapter 7.1
                        for (var j = 0; j < dataUnitLen; j++) buffer[i + j] = Hamming.Reverse8[buffer[i + j]];

                        // FIXME: This explicit type conversion could be a problem some day -- do not need to be platform independant
                        ProcessTelxPacket((DataUnitT)dataUnitId, new TeletextPacketPayload(buffer, i), _lastTimestamp);
                    }
                }

                i += dataUnitLen;
            }
        }

        static void AnalyzePat(byte[] buffer, int size)
        {
            if (size < 7) return;

            var pat = new Pat { PointerField = buffer[0] };

            // FIXME
            if (pat.PointerField > 0)
            {
                Console.WriteLine($"! pat.PointerField > 0 ({pat.PointerField})");
                return;
            }

            pat.TableId = buffer[1];
            if (pat.TableId == 0x00)
            {
                pat.SectionLength = ((buffer[2] & 0x03) << 8) | buffer[3];
                pat.CurrentNextIndicator = buffer[6] & 0x01;
                // already valid PAT
                if (pat.CurrentNextIndicator == 1)
                {
                    var i = 9;
                    while (i < 9 + (pat.SectionLength - 5 - 4) && i < size)
                    {
                        var section = new PatSection
                        {
                            ProgramNum = (buffer[i] << 8) | buffer[i + 1],
                            ProgramPid = ((buffer[i + 2] & 0x1f) << 8) | buffer[i + 3]
                        };

                        if (!InArray(PmtMap, _pmtMapCount, section.ProgramPid))
                        {
                            if (_pmtMapCount < TsPmtMapSize)
                            {
                                PmtMap[_pmtMapCount++] = section.ProgramPid;
                                //#if DEBUG
                                //                                Console.WriteLine($"# Found PMT for SID {section.program_num} ({section.program_num})");
                                //#endif
                            }
                        }
                        i += 4;
                    }
                }
            }
        }

        static void AnalyzePmt(byte[] buffer, int size)
        {
            if (size < 7) return;

            var pmt = new Pmt { PointerField = buffer[0] };

            // FIXME
            if (pmt.PointerField > 0)
            {
                Console.WriteLine($"! pmt.pointer_field > 0 ({pmt.PointerField})");
                return;
            }

            pmt.TableId = buffer[1];
            if (pmt.TableId == 0x02)
            {
                pmt.SectionLength = ((buffer[2] & 0x03) << 8) | buffer[3];
                pmt.ProgramNum = (buffer[4] << 8) | buffer[5];
                pmt.CurrentNextIndicator = buffer[6] & 0x01;
                pmt.PcrPid = ((buffer[9] & 0x1f) << 8) | buffer[10];
                pmt.ProgramInfoLength = ((buffer[11] & 0x03) << 8) | buffer[12];
                // already valid PMT
                if (pmt.CurrentNextIndicator == 1)
                {
                    var i = 13 + pmt.ProgramInfoLength;
                    while (i < 13 + (pmt.ProgramInfoLength + pmt.SectionLength - 4 - 9) && i < size)
                    {
                        var desc = new PmtProgramDescriptor
                        {
                            StreamType = buffer[i],
                            ElementaryPid = ((buffer[i + 1] & 0x1f) << 8) | buffer[i + 2],
                            EsInfoLength = ((buffer[i + 3] & 0x03) << 8) | buffer[i + 4]
                        };

                        var descriptorTag = buffer[i + 5];
                        // descriptor_tag: 0x45 = VBI_data_descriptor, 0x46 = VBI_teletext_descriptor, 0x56 = teletext_descriptor
                        if (desc.StreamType == 0x06 && (descriptorTag == 0x45 || descriptorTag == 0x46 || descriptorTag == 0x56))
                        {
                            if (!InArray(PmtTtxtMap, _pmtTtxtMapCount, desc.ElementaryPid))
                            {
                                if (_pmtTtxtMapCount < TsPmtTtxtMapSize)
                                {
                                    PmtTtxtMap[_pmtTtxtMapCount++] = desc.ElementaryPid;
                                    if (config.Tid == 0) config.Tid = desc.ElementaryPid;
                                    Console.WriteLine($"- Found VBI/teletext stream ID {desc.ElementaryPid} ({desc.ElementaryPid:X2}) for SID {pmt.ProgramNum} ({pmt.ProgramNum:X2})");
                                }
                            }
                        }

                        i += 5 + desc.EsInfoLength;
                    }
                }
            }
        }

        // graceful exit support
        private static bool ExitRequest = false;

        private static string GetBaseName()
        {
            return AppDomain.CurrentDomain.FriendlyName;
        }

        // main
        public static int RunMain(string[] args)
        {
            if (args.Length > 1 && args[1] == "-V")
            {
                Console.WriteLine(TelxccVersion);
                return ExitSuccess;
            }

            Console.WriteLine("telxcc - TELeteXt Closed Captions decoder");
            Console.WriteLine("(c) Forers, s. r. o., <info@forers.com>, 2011-2014; Licensed under the GPL.");
            Console.WriteLine($"Version {TelxccVersion}");
            Console.WriteLine();

            // command line params parsing
            int argIndex = 0;
            while (argIndex < args.Length)
            {
                var arg = args[argIndex];
                var argc = args.Length;
                if (arg == "-h")
                {
                    Console.WriteLine($"Usage: {GetBaseName()} -h");
                    Console.WriteLine($"  or   {GetBaseName()} -V");
                    Console.WriteLine($"  or   {GetBaseName()} [-v] [-m] [-i INPUT] [-o OUTPUT] [-p PAGE] [-t TID] [-f OFFSET] [-n] [-1] [-c] [-s [REF]]");
                    Console.WriteLine();
                    Console.WriteLine("  -h          this help text");
                    Console.WriteLine("  -V          print out version and quit");
                    Console.WriteLine("  -v          be verbose");
                    Console.WriteLine("  -m          input file format is BDAV MPEG-2 Transport Stream (BluRay and some IP-TV recorders)");
                    Console.WriteLine("  -i INPUT    transport stream (- = STDIN, default STDIN)");
                    Console.WriteLine("  -o OUTPUT   subtitles in SubRip SRT file format (UTF-8 encoded, NFC) (- = STDOUT, default STDOUT)");
                    Console.WriteLine("  -p PAGE     teletext page number carrying closed captions");
                    Console.WriteLine("  -t TID      transport stream PID of teletext data sub-stream");
                    Console.WriteLine("              if the value of 8192 is specified, the first suitable stream will be used");
                    Console.WriteLine("  -f OFFSET   subtitles offset in seconds");
                    Console.WriteLine("  -n          do not print UTF-8 BOM characters to the file");
                    Console.WriteLine("  -1          produce at least one (dummy) frame");
                    Console.WriteLine("  -c          output colour information in font HTML tags");
                    Console.WriteLine("  -s [REF]    search engine mode; produce absolute timestamps in UTC and output data in one line");
                    Console.WriteLine("              if REF (unix timestamp) is omitted, use current system time,");
                    Console.WriteLine("              telxcc will automatically switch to transport stream UTC timestamps when available");

                    return (ExitSuccess);
                }

                if (arg == "-i" && argc > argIndex + 1)
                {
                    config.InputName = args[++argIndex];
                }
                else if (arg == "-o" && argc > argIndex + 1)
                {
                    config.OutputName = args[++argIndex];
                }
                else if (arg == "-p" && argc > argIndex + 1)
                {
                    config.Page = Convert.ToInt32(args[++argIndex]);
                }
                else if (arg == "-t" && argc > argIndex + 1)
                {
                    config.Tid = Convert.ToInt32(args[++argIndex]);
                }
                else if (arg == "-f" && argc > argIndex + 1)
                {
                    config.Offset = Convert.ToInt32(args[++argIndex]);
                }
                else if (arg == "-n")
                {
                    config.Bom = false;
                }
                else if (arg == "-1")
                {
                    config.NonEmpty = true;
                }
                else if (arg == "-c")
                {
                    config.Colours = true;
                }
                else if (arg == "-v")
                {
                    config.Verbose = true;
                }
                else if (arg == "-s")
                {
                    config.SeMode = true;
                    ulong t = 0;
                    if (argc > argIndex + 1)
                    {
                        t = Convert.ToUInt64(args[argIndex + 1]);
                        if (t > 0) argIndex++;
                    }
                    if (t <= 0)
                    {
                        //time_t now = time(NULL);
                        t = 0;
                    }
                    config.UtcRefValue = t;
                }
                else if (arg == "-m")
                {
                    config.M2Ts = true;
                }
                else
                {
                    Console.WriteLine($"! Unknown option {arg}");
                    Console.WriteLine($"- For usage options run {GetBaseName()} -h");
                    return ExitFailure;
                }
                argIndex++;
            }

            if (config.M2Ts)
            {
                Console.WriteLine("- Processing input stream as a BDAV MPEG-2 Transport Stream");
            }

            if (config.SeMode)
            {
                var t0 = config.UtcRefValue;
                Console.WriteLine($"- Search engine mode active, UTC referential value = {t0}");
            }

            // teletext page number out of range
            if ((config.Page != 0) && ((config.Page < 100) || (config.Page > 899)))
            {
                Console.WriteLine("! Teletext page number could not be lower than 100 or higher than 899");
                return ExitFailure;
            }

            // default teletext page
            if (config.Page > 0)
            {
                // dec to BCD, magazine pages numbers are in BCD (ETSI 300 706)
                config.Page = ((config.Page / 100) << 8) | (((config.Page / 10) % 10) << 4) | (config.Page % 10);
            }

            // PID out of range
            if (config.Tid > 0x2000)
            {
                Console.WriteLine("! Transport stream PID could not be higher than 8192");
                return ExitFailure;
            }

            //signal(SIGINT, signal_handler);
            //signal(SIGTERM, signal_handler);

            if (string.IsNullOrEmpty(config.InputName) || config.InputName == "-")
            {
                Console.WriteLine($"! Please specify input file via the '-i <file name>' parameter");
                return ExitFailure;
            }
            try
            {
                _fin = new FileStream(config.InputName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch (Exception e)
            {
                Console.WriteLine($"! Could not open input file {config.InputName}: {e.Message}");
                return ExitFailure;
            }

            if (_fin.Length < 1) // isatty(fileno(fin)))
            {
                Console.WriteLine("! STDIN is a terminal. STDIN must be redirected.");
                return ExitFailure;
            }

            ////TODO: make last or use stream????
            //if (string.IsNullOrEmpty(config.output_name)  || config.output_name == "-")
            //{
            //    fout = Console.OpenStandardOutput();
            //}
            //else
            //{
            //    if ((fout = fopen(config.output_name, "wb")) == NULL)
            //    {
            //        fprintf(stderr, "! Could not open output file \"%s\".\n\n", config.output_name);
            //        goto fail;
            //    }
            //}

            //if (isatty(fileno(fout)))
            //{
            //    fprintf(stderr, "- STDOUT is a terminal, omitting UTF-8 BOM sequence on the output.\n");
            //    config.bom = NO;
            //}

            // full buffering -- disables flushing after CR/FL, we will flush manually whole SRT frames
            //setvbuf(fout, (char*)NULL, _IOFBF, 0);

            // print UTF-8 BOM chars
            if (config.Bom)
            {
                //fprintf(fout, "\xef\xbb\xbf");
                //fflush(fout);
            }

            // PROCESING

            // FYI, packet counter
            var packetCounter = 0;

            // TS packet buffer
            var tsPacketBuffer = new byte[TsPacketSize];
            var tsPacketSize = TsPacketSize - 4;

            // pointer to TS packet buffer start
            byte[] tsPacket = tsPacketBuffer;

            // if telxcc is configured to be in M2TS mode, it reads larger packets and ignores first 4 bytes
            if (config.M2Ts)
            {
                tsPacketSize = TsPacketSize;
                tsPacket = new byte[tsPacketSize];
                Buffer.BlockCopy(tsPacketBuffer, 4, tsPacket, 0, tsPacketSize);
            }

            // 0xff means not set yet
            var continuityCounter = 255;

            // PES packet buffer
            byte[] payloadBuffer = new byte[PayloadBufferSize];
            var payloadCounter = 0;

            // reading input
            while (!ExitRequest && _fin.Read(tsPacketBuffer, 0, tsPacketSize) == tsPacketSize)
            {
                // not TS packet -- misaligned?
                if (tsPacket[0] != SyncByte)
                {
                    Console.WriteLine("! Invalid TS packet header; TS seems to be misaligned");

                    int shift;
                    for (shift = 1; shift < TsPacketSize; shift++) if (tsPacket[shift] == SyncByte) break;

                    if (shift < TsPacketSize)
                    {
                        if (config.Verbose) Console.WriteLine($"! TS-packet-header-like byte found shifted by {shift} bytes, aligning TS stream (at least one TS packet lost)");
                        for (var i = shift; i < TsPacketSize; i++) tsPacket[i - shift] = tsPacket[i];
                        _fin.Read(tsPacketBuffer, 0, TsPacketSize - shift);
                    }
                }

                // Transport Stream Header
                // We do not use buffer to struct loading (e.g. ts_packet_t *header = (ts_packet_t *)ts_packet;)
                // -- struct packing is platform dependent and not performing well.
                var header = new TsPacket
                {
                    Sync = tsPacket[0],
                    TransportError = (tsPacket[1] & 0x80) >> 7,
                    PayloadUnitStart = (tsPacket[1] & 0x40) >> 6,
                    TransportPriority = (tsPacket[1] & 0x20) >> 5,
                    Pid = ((tsPacket[1] & 0x1f) << 8) | tsPacket[2],
                    ScramblingControl = (tsPacket[3] & 0xc0) >> 6,
                    AdaptationFieldExists = (tsPacket[3] & 0x20) >> 5,
                    ContinuityCounter = tsPacket[3] & 0x0f
                };
                //uint8_t ts_payload_exists = (ts_packet[3] & 0x10) >> 4;

                var afDiscontinuity = 0;
                if (header.AdaptationFieldExists > 0)
                {
                    afDiscontinuity = (tsPacket[5] & 0x80) >> 7;
                }

                // uncorrectable error?
                if (header.TransportError > 0)
                {
                    if (config.Verbose) Console.WriteLine($"! Uncorrectable TS packet error (received CC {header.ContinuityCounter})");
                    continue;
                }

                // if available, calculate current PCR
                if (header.AdaptationFieldExists > 0)
                {
                    // PCR in adaptation field
                    var afPcrExists = (tsPacket[5] & 0x10) >> 4;
                    if (afPcrExists > 0)
                    {
                        ulong pts = tsPacket[6];
                        pts <<= 25;
                        pts |= (ulong)(tsPacket[7] << 17);
                        pts |= (ulong)(tsPacket[8] << 9);
                        pts |= (ulong)(tsPacket[9] << 1);
                        pts |= (ulong)(tsPacket[10] >> 7);
                        _globalTimestamp = pts / 90;
                        pts = (ulong)((tsPacket[10] & 0x01) << 8);
                        pts |= tsPacket[11];
                        _globalTimestamp += pts / 27000;
                    }
                }

                // null packet
                if (header.Pid == 0x1fff) continue;

                // TID not specified, autodetect via PAT/PMT
                if (config.Tid == 0)
                {
                    // process PAT
                    if (header.Pid == 0x0000)
                    {
                        var patPacket = new byte[TsPacketPayloadSize];
                        Buffer.BlockCopy(tsPacket, 4, patPacket, 0, TsPacketPayloadSize);
                        AnalyzePat(patPacket, TsPacketPayloadSize);
                        continue;
                    }

                    // process PMT
                    if (InArray(PmtMap, _pmtMapCount, header.Pid))
                    {
                        var pmtPacket = new byte[TsPacketPayloadSize];
                        Buffer.BlockCopy(tsPacket, 4, pmtPacket, 0, TsPacketPayloadSize);
                        AnalyzePmt(pmtPacket, TsPacketPayloadSize);
                        continue;
                    }
                }

                // TID 0x2000 specified => dummy auto detection
                if (config.Tid == 0x2000)
                {
                    if (header.PayloadUnitStart > 0)
                    {
                        // searching for PES header and "Private Stream 1" stream_id
                        ulong pesPrefix = (ulong)((tsPacket[4] << 16) | (tsPacket[5] << 8) | tsPacket[6]);
                        var pesStreamId = tsPacket[7];

                        if (pesPrefix == 0x000001 && pesStreamId == 0xbd)
                        {
                            config.Tid = header.Pid;
                            Console.WriteLine($"- No teletext PID specified, first received suitable stream PID is {config.Tid} ({config.Tid:X2}), not guaranteed");
                            continue;
                        }
                    }
                }

                if (config.Tid == header.Pid)
                {
                    // TS continuity check
                    if (continuityCounter == 255) continuityCounter = header.ContinuityCounter;
                    else
                    {
                        if (afDiscontinuity == 0)
                        {
                            continuityCounter = (continuityCounter + 1) % 16;
                            if (header.ContinuityCounter != continuityCounter)
                            {
                                if (config.Verbose)
                                    Console.WriteLine($"- Missing TS packet, flushing pes_buffer (expected CC {continuityCounter}, received CC {header.ContinuityCounter}, TS discontinuity {(afDiscontinuity != 0 ? "YES" : "NO")}, TS priority {(header.TransportPriority != 0 ? "YES" : "NO")})");
                                payloadCounter = 0;
                                continuityCounter = 255;
                            }
                        }
                    }

                    // waiting for first payload_unit_start indicator
                    if (header.PayloadUnitStart == 0 && payloadCounter == 0) continue;

                    // proceed with payload buffer
                    if (header.PayloadUnitStart > 0 && payloadCounter > 0) ProcessPesPacket(payloadBuffer, payloadCounter);

                    // new payload frame start
                    if (header.PayloadUnitStart > 0) payloadCounter = 0;

                    // add payload data to buffer
                    if (payloadCounter < PayloadBufferSize - TsPacketPayloadSize)
                    {
                        Buffer.BlockCopy(tsPacket, 4, payloadBuffer, payloadCounter, TsPacketPayloadSize);
                        payloadCounter += TsPacketPayloadSize;
                        packetCounter++;
                    }
                    else if (config.Verbose) Console.WriteLine("! Packet payload size exceeds payload_buffer size, probably not teletext stream");
                }
            }

            // output any pending close caption
            if (PageBuffer.Tainted)
            {
                // this time we do not subtract any frames, there will be no more frames
                PageBuffer.HideTimestamp = _lastTimestamp;
                ProcessPage(PageBuffer);
            }

            if (config.Verbose)
            {
                if (config.Tid == 0)
                    Console.WriteLine($"- No teletext PID specified, no suitable PID found in PAT/PMT tables. Please specify teletext PID via -t parameter.{Environment.NewLine}  You can also specify -t 8192 for another type of autodetection (choosing the first suitable stream)");
                if (_framesProduced == 0)
                    Console.WriteLine("- No frames produced. CC teletext page number was probably wrong.");
                Console.Write("- There were some CC data carried via pages = ");
                // We ignore i = 0xff, because 0xffs are teletext ending frames
                for (var i = 0; i < 255; i++)
                {
                    for (var j = 0; j < 8; j++)
                    {
                        var v = CcMap[i] & (1 << j);
                        if (v > 0) Console.Write($"{((j + 1) << 8) | i:X2} ");
                    }
                }
                Console.WriteLine();
            }

            if (!config.SeMode && _framesProduced == 0 && config.NonEmpty)
            {
                Fout.AppendLine("1");
                Fout.AppendLine("00:00:00,000 --> 00:00:01,000");
                _framesProduced++;
            }

            Console.WriteLine($"- Done ({packetCounter:#,###,##0} teletext packets processed, {_framesProduced} frames produced)");
            Console.WriteLine("");

            if (string.IsNullOrEmpty(config.OutputName) || config.OutputName == "-")
            {
                Console.WriteLine(Fout.ToString());
            }
            else
            {
                try
                {
                    File.WriteAllText(config.OutputName, Fout.ToString(), new UTF8Encoding(config.Bom));
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Console.WriteLine($"! Could not write to output file {config.OutputName}: {e.Message}");
                    return ExitFailure;
                }
            }

            return ExitSuccess;
        }
    }
}
