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
        const string TELXCC_VERSION = "2.6.0";

        private const int SIGINT = 2;
        private const int SIGTERM = 15;

        private const int EXIT_SUCCESS = 0;
        private const int EXIT_FAILURE = 1;

        private const int SYNC_BYTE = 0x47;

        public enum bool_t
        {
            NO = 0x00,
            YES = 0x01,
            UNDEF = 0xff
        }

        // size of a (M2)TS packet in bytes (TS = 188, M2TS = 192)
        private const int TS_PACKET_SIZE = 192;

        // size of a TS packet payload in bytes
        private const int TS_PACKET_PAYLOAD_SIZE = 184;

        // size of a packet payload buffer
        private const int PAYLOAD_BUFFER_SIZE = 4096;

        public class ts_packet_t
        {
            public int sync;
            public int transport_error;
            public int payload_unit_start;
            public int transport_priority;
            public int pid;
            public int scrambling_control;
            public int adaptation_field_exists;
            public int continuity_counter;
        }

        public class pat_section_t
        {
            public int program_num;
            public int program_pid;
        }

        public class pat_t
        {
            public int pointer_field;
            public int table_id;
            public int section_length;
            public int current_next_indicator;
        }

        public class pmt_program_descriptor_t
        {
            public int stream_type;
            public int elementary_pid;
            public int es_info_length;
        }

        public class pmt_t
        {
            public int pointer_field;
            public int table_id;
            public int section_length;
            public int program_num;
            public int current_next_indicator;
            public int pcr_pid;
            public int program_info_length;
        }

        public enum data_unit_t
        {
            DATA_UNIT_EBU_TELETEXT_NONSUBTITLE = 0x02,
            DATA_UNIT_EBU_TELETEXT_SUBTITLE = 0x03,
            DATA_UNIT_EBU_TELETEXT_INVERTED = 0x0c,
            DATA_UNIT_VPS = 0xc3,
            DATA_UNIT_CLOSED_CAPTIONS = 0xc5
        }

        public enum transmission_mode_t
        {
            TRANSMISSION_MODE_PARALLEL = 0,
            TRANSMISSION_MODE_SERIAL = 1
        }

        private static string[] TTXT_COLOURS = new string[8]
        {
            //black,   red,       green,     yellow,    blue,      magenta,   cyan,      white
            "#000000", "#ff0000", "#00ff00", "#ffff00", "#0000ff", "#ff00ff", "#00ffff", "#ffffff"
        };

        public class teletext_packet_payload_t
        {
            public int _clock_in; // clock run in
            public int _framing_code; // framing code, not needed, ETSI 300 706: const 0xe4
            public byte[] address = new byte[2];
            public byte[] data = new byte[40];

            public teletext_packet_payload_t(byte[] buffer, int index)
            {
                _clock_in = buffer[index];
                _framing_code = buffer[index + 1];
                address[0] = buffer[index + 2];
                address[1] = buffer[index + 3];
                Buffer.BlockCopy(buffer, index + 4, data, 0, data.Length);
            }
        }

        public class teletext_page_t
        {
            public ulong show_timestamp; // show at timestamp (in ms)
            public ulong hide_timestamp; // hide at timestamp (in ms)
            public int[,] text = new int[25, 40]; // 25 lines x 40 cols (1 screen/page) of wide chars
            public int tainted; // 1 = text variable contains any data
        }

        public class frame_t
        {
            public ulong show_timestamp; // show at timestamp (in ms)
            public ulong hide_timestamp; // hide at timestamp (in ms)
            public string text;
        }

        // application config global variable
        public class Config
        {
            public string input_name; // input file name
            public string output_name; // output file name
            public bool verbose; // should telxcc be verbose?
            public int page; // teletext page containing cc we want to filter
            public int tid; // 13-bit packet ID for teletext stream
            public double offset; // time offset in seconds
            public bool colours; // output <font...></font> tags
            public bool bom; // print UTF-8 BOM characters at the beginning of output
            public bool nonempty; // produce at least one (dummy) frame
            public ulong utc_refvalue; // UTC referential value

            public bool se_mode; // FIXME: move SE_MODE to output module

            //char *template; // output format template
            public bool m2ts; // consider input stream is af s M2TS, instead of TS

            public Config()
            {
                input_name = null;
                output_name = null;
                verbose = false;
                page = 0;
                tid = 0;
                offset = 0;
                colours = false;
                bom = true;
                nonempty = false;
                utc_refvalue = 0;
                se_mode = false;
                //.template = NULL,
                m2ts = false;
            }
        }

        private static readonly Config config = new Config();

        /*
        formatting template:
            %f -- from timestamp (absolute, UTC)
            %t -- to timestamp (absolute, UTC)
            %F -- from time (SRT)
            %T -- to time (SRT)
            %g -- from timestamp (relative)
            %u -- to timestamp (relative)
            %c -- counter 0-based
            %C -- counter 1-based
            %s -- subtitles
            %l -- subtitles (lines)
            %p -- page number
            %i -- stream ID
        */

        private static Stream fin;
        private static StringBuilder fout = new StringBuilder();

        // application states -- flags for notices that should be printed only once
        public class States
        {
            public bool programme_info_processed;
            public bool pts_initialized;
        }
        private static States states = new States();

        // SRT frames produced
        private static int frames_produced;

        // subtitle type pages bitmap, 2048 bits = 2048 possible pages in teletext (excl. subpages)
        private static byte[] cc_map = new byte[256];

        // global TS PCR value
        private static ulong global_timestamp;

        // last timestamp computed
        private static ulong last_timestamp;

        // working teletext page buffer
        private static teletext_page_t page_buffer = new teletext_page_t();

        // teletext transmission mode
        private static transmission_mode_t transmission_mode = transmission_mode_t.TRANSMISSION_MODE_SERIAL;

        // flag indicating if incoming data should be processed or ignored
        private static bool receiving_data;

        // current charset (charset can be -- and always is -- changed during transmission)
        public class PrimaryCharset
        {
            public int current;
            public int g0_m29;
            public int g0_x28;

            public PrimaryCharset()
            {
                current = 0x00;
                g0_m29 = (int)bool_t.UNDEF;
                g0_x28 = (int)bool_t.UNDEF;
            }
        }
        private static readonly PrimaryCharset primaryCharset = new PrimaryCharset();

        // entities, used in colour mode, to replace unsafe HTML tag chars
        private static readonly Dictionary<char, string> Entities = new Dictionary<char, string>()
        {
            { '<', "&lt;" },
            { '>', "&gt;" },
            { '&', "&amp;" }
        };

        // PMTs table
        private const int TS_PMT_MAP_SIZE = 128;
        private static int[] pmt_map = new int[TS_PMT_MAP_SIZE];
        private static int pmt_map_count;

        // TTXT streams table
        private const int TS_PMT_TTXT_MAP_SIZE = 128;
        private static int[] pmt_ttxt_map = new int[TS_PMT_MAP_SIZE];
        private static int pmt_ttxt_map_count;

        // helper, linear searcher for a value
        private static bool_t in_array(int[] array, int length, int element)
        {
            bool_t r = bool_t.NO;
            for (var i = 0; i < length; i++)
            {
                if (array[i] == element)
                {
                    r = bool_t.YES;
                    break;
                }
            }
            return r;
        }

        // extracts magazine number from teletext page
        private static int MAGAZINE(int p)
        {
            return (p >> 8) & 0xf;
        }

        // extracts page number from teletext page
        private static int PAGE(int p)
        {
            return p & 0xff;
        }

        // ETS 300 706, chapter 8.2
        private static byte unham_8_4(byte a)
        {
            var r = Hamming.Unham84[a];
            if (r == 0xff)
            {
                r = 0;
                if (config.verbose)
                {
                    Console.WriteLine($"! Unrecoverable data error; UNHAM8/4({a:X2})");
                }
            }
            return (byte)(r & 0x0f);
        }

        // ETS 300 706, chapter 8.3
        private static uint unham_24_18(int a)
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

        private static void remap_g0_charset(int c)
        {
            if (c != primaryCharset.current)
            {
                var m = Tables.G0_LATIN_NATIONAL_SUBSETS_MAP[c];
                if (m == 0xff)
                {
                    Console.WriteLine($"- G0 Latin National Subset ID {(c >> 3):X2}.{(c & 0x7):X2} is not implemented");
                }
                else
                {
                    for (int j = 0; j < 13; j++) Tables.G0[(int)Tables.g0_charsets_t.LATIN, Tables.G0_LATIN_NATIONAL_SUBSETS_POSITIONS[j]] = Tables.G0_LATIN_NATIONAL_SUBSETS[m].Characters[j];
                    if (config.verbose) Console.WriteLine($"- Using G0 Latin National Subset ID {(c >> 3):X2}.{(c & 0x7):X2} ({Tables.G0_LATIN_NATIONAL_SUBSETS[m].Language})");
                    primaryCharset.current = c;
                }
            }
        }

        private static string timestamp_to_srttime(ulong timestamp)
        {
            var p = timestamp;
            var h = p / 3600000;
            var m = p / 60000 - 60 * h;
            var s = p / 1000 - 3600 * h - 60 * m;
            var u = p - 3600000 * h - 60000 * m - 1000 * s;
            return $"{h:00}:{m:00}:{s:00},{u:000}";
        }

        // UCS-2 (16 bits) to UTF-8 (Unicode Normalization Form C (NFC)) conversion
        private static string ucs2_to_utf8(int ch)
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
        private static int telx_to_ucs2(byte c)
        {
            if (Hamming.Parity8[c] == 0)
            {
                if (config.verbose) Console.WriteLine($"! Unrecoverable data error; PARITY({c:X2})");
                return 0x20;
            }

            var r = c & 0x7f;
            if (r >= 0x20) r = Tables.G0[(int)Tables.g0_charsets_t.LATIN, r - 0x20];
            return r;
        }

        // FIXME: implement output modules (to support different formats, printf formatting etc)
        static void process_page(teletext_page_t page)
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
            bool page_is_empty = true;
            for (var col = 0; col < 40; col++)
            {
                for (var row = 1; row < 25; row++)
                {
                    if (page.text[row, col] == 0x0b)
                    {
                        page_is_empty = false;
                        goto page_is_empty;
                    }
                }
            }
            page_is_empty:
            if (page_is_empty) return;

            if (page.show_timestamp > page.hide_timestamp) page.hide_timestamp = page.show_timestamp;

            if (config.se_mode)
            {
                ++frames_produced;
                fout.Append($"{(double)page.show_timestamp / 1000.0}|");
            }
            else
            {
                var timeCodeShow = timestamp_to_srttime(page.show_timestamp);
                var timeCodeHide = timestamp_to_srttime(page.hide_timestamp);
                fout.AppendLine($"{++frames_produced}{Environment.NewLine}{timeCodeShow} --> {timeCodeHide}");
            }

            // process data
            for (var row = 1; row < 25; row++)
            {
                // anchors for string trimming purpose
                var col_start = 40;
                var col_stop = 40;

                for (var col = 39; col >= 0; col--)
                {
                    if (page.text[row, col] == 0xb)
                    {
                        col_start = col;
                        break;
                    }
                }
                // line is empty
                if (col_start > 39) continue;

                for (var col = col_start + 1; col <= 39; col++)
                {
                    if (page.text[row, col] > 0x20)
                    {
                        if (col_stop > 39) col_start = col;
                        col_stop = col;
                    }
                    if (page.text[row, col] == 0xa) break;
                }
                // line is empty
                if (col_stop > 39) continue;

                // ETS 300 706, chapter 12.2: Alpha White ("Set-After") - Start-of-row default condition.
                // used for colour changes _before_ start box mark
                // white is default as stated in ETS 300 706, chapter 12.2
                // black(0), red(1), green(2), yellow(3), blue(4), magenta(5), cyan(6), white(7)
                var foreground_color = 0x7;
                bool font_tag_opened = false;

                for (var col = 0; col <= col_stop; col++)
                {
                    // v is just a shortcut
                    var v = page.text[row, col];

                    if (col < col_start)
                    {
                        if (v <= 0x7) foreground_color = v;
                    }

                    if (col == col_start)
                    {
                        if ((foreground_color != 0x7) && (config.colours))
                        {
                            fout.Append($"<font color=\"{TTXT_COLOURS[foreground_color]}\">");
                            font_tag_opened = true;
                        }
                    }

                    if (col >= col_start)
                    {
                        if (v <= 0x7)
                        {
                            // ETS 300 706, chapter 12.2: Unless operating in "Hold Mosaics" mode,
                            // each character space occupied by a spacing attribute is displayed as a SPACE.
                            if (config.colours)
                            {
                                if (font_tag_opened)
                                {
                                    fout.Append("</font> ");
                                    font_tag_opened = false;
                                }

                                // black is considered as white for telxcc purpose
                                // telxcc writes <font/> tags only when needed
                                if ((v > 0x0) && (v < 0x7))
                                {
                                    fout.Append($"<font color=\"{TTXT_COLOURS[v]}\">");
                                    font_tag_opened = true;
                                }
                            }
                            else v = 0x20;
                        }

                        if (v >= 0x20)
                        {
                            // translate some chars into entities, if in colour mode
                            if (config.colours)
                            {
                                if (Entities.ContainsKey(Convert.ToChar(v)))
                                {
                                    fout.Append(Entities[Convert.ToChar(v)]);
                                    // v < 0x20 won't be printed in next block
                                    v = 0;
                                    break;
                                }
                            }
                        }

                        if (v >= 0x20)
                        {
                            fout.Append(ucs2_to_utf8(v));
                        }
                    }
                }

                // no tag will left opened!
                if ((config.colours) && (font_tag_opened))
                {
                    fout.Append("</font>");
                    font_tag_opened = false;
                }

                // line delimiter
                fout.Append((config.se_mode) ? " " : Environment.NewLine);
            }
            fout.AppendLine();
        }

        private static void process_telx_packet(data_unit_t data_unit_id, teletext_packet_payload_t packet, ulong timestamp)
        {
            // variable names conform to ETS 300 706, chapter 7.1.2
            var address = (unham_8_4(packet.address[1]) << 4) | unham_8_4(packet.address[0]);
            var m = address & 0x7;
            if (m == 0) m = 8;
            var y = (address >> 3) & 0x1f;
            var designation_code = (y > 25) ? unham_8_4(packet.data[0]) : 0x00;

            if (y == 0)
            {
                // CC map
                var i = (unham_8_4(packet.data[1]) << 4) | unham_8_4(packet.data[0]);
                var flag_subtitle = (unham_8_4(packet.data[5]) & 0x08) >> 3;
                cc_map[i] |= (byte)(flag_subtitle << (m - 1));

                if (config.page == 0 && flag_subtitle == (int)bool_t.YES && i < 0xff)
                {
                    config.page = (m << 8) | (unham_8_4(packet.data[1]) << 4) | unham_8_4(packet.data[0]);
                    Console.WriteLine($"- No teletext page specified, first received suitable page is {config.page}, not guaranteed");
                }

                // Page number and control bits
                var page_number = (m << 8) | (unham_8_4(packet.data[1]) << 4) | unham_8_4(packet.data[0]);
                var charset = ((unham_8_4(packet.data[7]) & 0x08) | (unham_8_4(packet.data[7]) & 0x04) | (unham_8_4(packet.data[7]) & 0x02)) >> 1;
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
                transmission_mode = (transmission_mode_t)(unham_8_4(packet.data[7]) & 0x01);

                // FIXME: Well, this is not ETS 300 706 kosher, however we are interested in DATA_UNIT_EBU_TELETEXT_SUBTITLE only
                if ((transmission_mode == transmission_mode_t.TRANSMISSION_MODE_PARALLEL) && (data_unit_id != data_unit_t.DATA_UNIT_EBU_TELETEXT_SUBTITLE)) return;

                if ((receiving_data) && (
                        ((transmission_mode == transmission_mode_t.TRANSMISSION_MODE_SERIAL) && (PAGE(page_number) != PAGE(config.page))) ||
                        ((transmission_mode == transmission_mode_t.TRANSMISSION_MODE_PARALLEL) && (PAGE(page_number) != PAGE(config.page)) && (m == MAGAZINE(config.page)))
                    ))
                {
                    receiving_data = false;
                    return;
                }

                // Page transmission is terminated, however now we are waiting for our new page
                if (page_number != config.page) return;

                // Now we have the beginning of page transmission; if there is page_buffer pending, process it
                if (page_buffer.tainted == (int)bool_t.YES)
                {
                    // it would be nice, if subtitle hides on previous video frame, so we contract 40 ms (1 frame @25 fps)
                    page_buffer.hide_timestamp = timestamp - 40;
                    process_page(page_buffer);
                }

                page_buffer.show_timestamp = timestamp;
                page_buffer.hide_timestamp = 0;
                page_buffer.text = new int[25, 40]; //memset(page_buffer.text, 0x00, sizeof(page_buffer.text));
                page_buffer.tainted = (int)bool_t.NO;
                receiving_data = true;
                primaryCharset.g0_x28 = (int)bool_t.UNDEF;

                var c = (primaryCharset.g0_m29 != (int)bool_t.UNDEF) ? primaryCharset.g0_m29 : charset;
                remap_g0_charset(c);

                /*
                // I know -- not needed; in subtitles we will never need disturbing teletext page status bar
                // displaying tv station name, current time etc.
                if (flag_suppress_header == NO) {
                    for (uint8_t i = 14; i < 40; i++) page_buffer.text[y,i] = telx_to_ucs2(packet.data[i]);
                    //page_buffer.tainted = YES;
                }
                */
            }
            else if ((m == MAGAZINE(config.page)) && (y >= 1) && (y <= 23) && (receiving_data))
            {
                // ETS 300 706, chapter 9.4.1: Packets X/26 at presentation Levels 1.5, 2.5, 3.5 are used for addressing
                // a character location and overwriting the existing character defined on the Level 1 page
                // ETS 300 706, annex B.2.2: Packets with Y = 26 shall be transmitted before any packets with Y = 1 to Y = 25;
                // so page_buffer.text[y,i] may already contain any character received
                // in frame number 26, skip original G0 character
                for (var i = 0; i < 40; i++) if (page_buffer.text[y, i] == 0x00) page_buffer.text[y, i] = telx_to_ucs2(packet.data[i]);
                page_buffer.tainted = (int)bool_t.YES;
            }
            else if ((m == MAGAZINE(config.page)) && (y == 26) && (receiving_data))
            {
                // ETS 300 706, chapter 12.3.2: X/26 definition
                var x26_row = 0;
                var x26_col = 0;

                var triplets = new uint[13];
                var j = 0;
                for (var i = 1; i < 40; i += 3, j++) triplets[j] = unham_24_18((packet.data[i + 2] << 16) | (packet.data[i + 1] << 8) | packet.data[i]);

                for (var j2 = 0; j2 < 13; j2++)
                {
                    if (triplets[j2] == 0xffffffff)
                    {
                        // invalid data (HAM24/18 uncorrectable error detected), skip group
                        if (config.verbose) Console.WriteLine($"! Unrecoverable data error; UNHAM24/18()={triplets[j2]}");
                        continue;
                    }

                    var data = (triplets[j2] & 0x3f800) >> 11;
                    var mode = (triplets[j2] & 0x7c0) >> 6;
                    var address2 = triplets[j2] & 0x3f;
                    var row_address_group = (address2 >= 40) && (address2 <= 63);

                    // ETS 300 706, chapter 12.3.1, table 27: set active position
                    if ((mode == 0x04) && (row_address_group))
                    {
                        x26_row = (int)(address2 - 40);
                        if (x26_row == 0) x26_row = 24;
                        x26_col = 0;
                    }

                    // ETS 300 706, chapter 12.3.1, table 27: termination marker
                    if ((mode >= 0x11) && (mode <= 0x1f) && (row_address_group)) break;

                    // ETS 300 706, chapter 12.3.1, table 27: character from G2 set
                    if ((mode == 0x0f) && (!row_address_group))
                    {
                        x26_col = (int)address2;
                        if (data > 31) page_buffer.text[x26_row, x26_col] = Tables.G2[0, data - 0x20];
                    }

                    // ETS 300 706, chapter 12.3.1, table 27: G0 character with diacritical mark
                    if ((mode >= 0x11) && (mode <= 0x1f) && (!row_address_group))
                    {
                        x26_col = (int)address2;

                        // A - Z
                        if ((data >= 65) && (data <= 90)) page_buffer.text[x26_row, x26_col] = Tables.G2_ACCENTS[mode - 0x11, data - 65];
                        // a - z
                        else if ((data >= 97) && (data <= 122)) page_buffer.text[x26_row, x26_col] = Tables.G2_ACCENTS[mode - 0x11, data - 71];
                        // other
                        else page_buffer.text[x26_row, x26_col] = telx_to_ucs2((byte)data);
                    }
                }
            }
            else if ((m == MAGAZINE(config.page)) && (y == 28) && (receiving_data))
            {
                // TODO:
                //   ETS 300 706, chapter 9.4.7: Packet X/28/4
                //   Where packets 28/0 and 28/4 are both transmitted as part of a page, packet 28/0 takes precedence over 28/4 for all but the colour map entry coding.
                if ((designation_code == 0) || (designation_code == 4))
                {
                    // ETS 300 706, chapter 9.4.2: Packet X/28/0 Format 1
                    // ETS 300 706, chapter 9.4.7: Packet X/28/4
                    uint triplet0 = unham_24_18((packet.data[3] << 16) | (packet.data[2] << 8) | packet.data[1]);

                    if (triplet0 == 0xffffffff)
                    {
                        // invalid data (HAM24/18 uncorrectable error detected), skip group
                        if (config.verbose) Console.WriteLine($"! Unrecoverable data error; UNHAM24/18()={triplet0}");
                    }
                    else
                    {
                        // ETS 300 706, chapter 9.4.2: Packet X/28/0 Format 1 only
                        if ((triplet0 & 0x0f) == 0x00)
                        {
                            primaryCharset.g0_x28 = (int)((triplet0 & 0x3f80) >> 7);
                            remap_g0_charset(primaryCharset.g0_x28);
                        }
                    }
                }
            }
            else if ((m == MAGAZINE(config.page)) && (y == 29))
            {
                // TODO:
                //   ETS 300 706, chapter 9.5.1 Packet M/29/0
                //   Where M/29/0 and M/29/4 are transmitted for the same magazine, M/29/0 takes precedence over M/29/4.
                if ((designation_code == 0) || (designation_code == 4))
                {
                    // ETS 300 706, chapter 9.5.1: Packet M/29/0
                    // ETS 300 706, chapter 9.5.3: Packet M/29/4
                    uint triplet0 = unham_24_18((packet.data[3] << 16) | (packet.data[2] << 8) | packet.data[1]);

                    if (triplet0 == 0xffffffff)
                    {
                        // invalid data (HAM24/18 uncorrectable error detected), skip group
                        if (config.verbose) Console.WriteLine($"! Unrecoverable data error; UNHAM24/18()={triplet0}");
                    }
                    else
                    {
                        // ETS 300 706, table 11: Coding of Packet M/29/0
                        // ETS 300 706, table 13: Coding of Packet M/29/4
                        if ((triplet0 & 0xff) == 0x00)
                        {
                            primaryCharset.g0_m29 = (int)((triplet0 & 0x3f80) >> 7);
                            // X/28 takes precedence over M/29
                            if (primaryCharset.g0_x28 == (int)bool_t.UNDEF)
                            {
                                remap_g0_charset(primaryCharset.g0_m29);
                            }
                        }
                    }
                }
            }
            else if ((m == 8) && (y == 30))
            {
                // ETS 300 706, chapter 9.8: Broadcast Service Data Packets
                if (!states.programme_info_processed)
                {
                    // ETS 300 706, chapter 9.8.1: Packet 8/30 Format 1
                    if (unham_8_4(packet.data[0]) < 2)
                    {
                        Console.Write("- Programme Identification Data = ");
                        for (var i = 20; i < 40; i++)
                        {
                            var c = telx_to_ucs2(packet.data[i]);
                            // strip any control codes from PID, eg. TVP station
                            if (c < 0x20) continue;

                            Console.Write(ucs2_to_utf8(c));
                        }
                        Console.WriteLine();

                        // OMG! ETS 300 706 stores timestamp in 7 bytes in Modified Julian Day in BCD format + HH:MM:SS in BCD format
                        // + timezone as 5-bit count of half-hours from GMT with 1-bit sign
                        // In addition all decimals are incremented by 1 before transmission.
                        long t = 0;
                        // 1st step: BCD to Modified Julian Day
                        t += (packet.data[10] & 0x0f) * 10000;
                        t += ((packet.data[11] & 0xf0) >> 4) * 1000;
                        t += (packet.data[11] & 0x0f) * 100;
                        t += ((packet.data[12] & 0xf0) >> 4) * 10;
                        t += (packet.data[12] & 0x0f);
                        t -= 11111;
                        // 2nd step: conversion Modified Julian Day to unix timestamp
                        t = (t - 40587) * 86400;
                        // 3rd step: add time
                        t += 3600 * (((packet.data[13] & 0xf0) >> 4) * 10 + (packet.data[13] & 0x0f));
                        t += 60 * (((packet.data[14] & 0xf0) >> 4) * 10 + (packet.data[14] & 0x0f));
                        t += (((packet.data[15] & 0xf0) >> 4) * 10 + (packet.data[15] & 0x0f));
                        t -= 40271;
                        // 4th step: conversion to time_t
                        var span = TimeSpan.FromTicks(t * TimeSpan.TicksPerSecond);
                        var t2 = new DateTime(1970, 1, 1).Add(span);
                        var localTime = TimeZone.CurrentTimeZone.ToLocalTime(t2); // TimeZone.CurrentTimeZone.ToUniversalTime(t2); ?
                        
                        Console.WriteLine($"- Programme Timestamp (UTC) = {localTime.ToLongDateString()} {localTime.ToLongTimeString()}");

                        if (config.verbose) Console.WriteLine($"- Transmission mode = {(transmission_mode == transmission_mode_t.TRANSMISSION_MODE_SERIAL ? "serial" : "parallel")}");

                        if (config.se_mode)
                        {
                            Console.WriteLine($"- Broadcast Service Data Packet received, resetting UTC referential value to {t} seconds");
                            config.utc_refvalue = (ulong)t;
                            states.pts_initialized = false;
                        }

                        states.programme_info_processed = true;
                    }
                }
            }
        }

        static bool_t using_pts = bool_t.UNDEF;
        static long delta = 0;
        static long t0 = 0;

        static void process_pes_packet(byte[] buffer, int size)
        {
            if (size < 6) return;

            // Packetized Elementary Stream (PES) 32-bit start code
            ulong pes_prefix = (ulong)((buffer[0] << 16) | (buffer[1] << 8) | buffer[2]);
            var pes_stream_id = buffer[3];

            // check for PES header
            if (pes_prefix != 0x000001) return;

            // stream_id is not "Private Stream 1" (0xbd)
            if (pes_stream_id != 0xbd) return;

            // PES packet length
            // ETSI EN 301 775 V1.2.1 (2003-05) chapter 4.3: (N x 184) - 6 + 6 B header
            var pes_packet_length = 6 + ((buffer[4] << 8) | buffer[5]);
            // Can be zero. If the "PES packet length" is set to zero, the PES packet can be of any length.
            // A value of zero for the PES packet length can be used only when the PES packet payload is a video elementary stream.
            if (pes_packet_length == 6) return;

            // truncate incomplete PES packets
            if (pes_packet_length > size) pes_packet_length = size;

            bool optional_pes_header_included = false;
            var optional_pes_header_length = 0;
            // optional PES header marker bits (10.. ....)
            if ((buffer[6] & 0xc0) == 0x80)
            {
                optional_pes_header_included = true;
                optional_pes_header_length = buffer[8];
            }

            // should we use PTS or PCR?
            if (using_pts == bool_t.UNDEF)
            {
                if ((optional_pes_header_included) && ((buffer[7] & 0x80) > 0))
                {
                    using_pts = bool_t.YES;
                    if (config.verbose) Console.WriteLine("- PID 0xbd PTS available");
                }
                else
                {
                    using_pts = bool_t.NO;
                    if (config.verbose) Console.WriteLine(" - PID 0xbd PTS unavailable, using TS PCR");
                }
            }

            ulong t = 0;
            // If there is no PTS available, use global PCR
            if (using_pts == bool_t.NO)
            {
                t = global_timestamp;
            }
            else
            {
                // PTS is 33 bits wide, however, timestamp in ms fits into 32 bits nicely (PTS/90)
                // presentation and decoder timestamps use the 90 KHz clock, hence PTS/90 = [ms]
                // __MUST__ assign value to uint64_t and __THEN__ rotate left by 29 bits
                // << is defined for signed int (as in "C" spec.) and overflow occures
                long pts = (buffer[9] & 0x0e);
                pts <<= 29;
                pts |= (buffer[10] << 22);
                pts |= ((buffer[11] & 0xfe) << 14);
                pts |= (buffer[12] << 7);
                pts |= ((buffer[13] & 0xfe) >> 1);
                t = (ulong)pts / 90;
            }

            if (!states.pts_initialized)
            {
                delta = (long)(1000 * config.offset + 1000 * config.utc_refvalue - t);
                states.pts_initialized = true;

                if ((using_pts == bool_t.NO) && (global_timestamp == 0))
                {
                    // We are using global PCR, nevertheless we still have not received valid PCR timestamp yet
                    states.pts_initialized = false;
                }
            }
            if (t < (ulong)t0) delta = (long)last_timestamp;
            last_timestamp = t + (ulong)delta;
            t0 = (long)t;

            // skip optional PES header and process each 46 bytes long teletext packet
            var i = 7;
            if (optional_pes_header_included) i += 3 + optional_pes_header_length;
            while (i <= pes_packet_length - 6)
            {
                var data_unit_id = buffer[i++];
                var data_unit_len = buffer[i++];

                if ((data_unit_id == (int)data_unit_t.DATA_UNIT_EBU_TELETEXT_NONSUBTITLE) || (data_unit_id == (int)data_unit_t.DATA_UNIT_EBU_TELETEXT_SUBTITLE))
                {
                    // teletext payload has always size 44 bytes
                    if (data_unit_len == 44)
                    {
                        // reverse endianess (via lookup table), ETS 300 706, chapter 7.1
                        for (var j = 0; j < data_unit_len; j++) buffer[i + j] = Hamming.Reverse8[buffer[i + j]];

                        // FIXME: This explicit type conversion could be a problem some day -- do not need to be platform independant
                        process_telx_packet((data_unit_t)data_unit_id, new teletext_packet_payload_t(buffer, i), last_timestamp);
                    }
                }

                i += data_unit_len;
            }
        }

        static void analyze_pat(byte[] buffer, int size)
        {
            if (size < 7) return;

            var pat = new pat_t { pointer_field = buffer[0] };

            // FIXME
            if (pat.pointer_field > 0)
            {
                Console.WriteLine($"! pat.pointer_field > 0 ({pat.pointer_field})");
                return;
            }

            pat.table_id = buffer[1];
            if (pat.table_id == 0x00)
            {
                pat.section_length = ((buffer[2] & 0x03) << 8) | buffer[3];
                pat.current_next_indicator = buffer[6] & 0x01;
                // already valid PAT
                if (pat.current_next_indicator == 1)
                {
                    var i = 9;
                    while ((i < 9 + (pat.section_length - 5 - 4)) && (i < size))
                    {
                        var section = new pat_section_t
                        {
                            program_num = (buffer[i] << 8) | buffer[i + 1],
                            program_pid = ((buffer[i + 2] & 0x1f) << 8) | buffer[i + 3]
                        };

                        if (in_array(pmt_map, pmt_map_count, section.program_pid) == bool_t.NO)
                        {
                            if (pmt_map_count < TS_PMT_MAP_SIZE)
                            {
                                pmt_map[pmt_map_count++] = section.program_pid;
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

        static void analyze_pmt(byte[] buffer, int size)
        {
            if (size < 7) return;

            var pmt = new pmt_t { pointer_field = buffer[0] };

            // FIXME
            if (pmt.pointer_field > 0)
            {
                Console.WriteLine($"! pmt.pointer_field > 0 ({pmt.pointer_field})");
                return;
            }

            pmt.table_id = buffer[1];
            if (pmt.table_id == 0x02)
            {
                pmt.section_length = ((buffer[2] & 0x03) << 8) | buffer[3];
                pmt.program_num = (buffer[4] << 8) | buffer[5];
                pmt.current_next_indicator = buffer[6] & 0x01;
                pmt.pcr_pid = ((buffer[9] & 0x1f) << 8) | buffer[10];
                pmt.program_info_length = ((buffer[11] & 0x03) << 8) | buffer[12];
                // already valid PMT
                if (pmt.current_next_indicator == 1)
                {
                    var i = 13 + pmt.program_info_length;
                    while ((i < 13 + (pmt.program_info_length + pmt.section_length - 4 - 9)) && (i < size))
                    {
                        var desc = new pmt_program_descriptor_t
                        {
                            stream_type = buffer[i],
                            elementary_pid = ((buffer[i + 1] & 0x1f) << 8) | buffer[i + 2],
                            es_info_length = ((buffer[i + 3] & 0x03) << 8) | buffer[i + 4]
                        };

                        var descriptor_tag = buffer[i + 5];
                        // descriptor_tag: 0x45 = VBI_data_descriptor, 0x46 = VBI_teletext_descriptor, 0x56 = teletext_descriptor
                        if ((desc.stream_type == 0x06) && ((descriptor_tag == 0x45) || (descriptor_tag == 0x46) || (descriptor_tag == 0x56)))
                        {
                            if (in_array(pmt_ttxt_map, pmt_ttxt_map_count, desc.elementary_pid) == bool_t.NO)
                            {
                                if (pmt_ttxt_map_count < TS_PMT_TTXT_MAP_SIZE)
                                {
                                    pmt_ttxt_map[pmt_ttxt_map_count++] = desc.elementary_pid;
                                    if (config.tid == 0) config.tid = desc.elementary_pid;
                                    Console.WriteLine($"- Found VBI/teletext stream ID {desc.elementary_pid} ({desc.elementary_pid:X2}) for SID {pmt.program_num} ({pmt.program_num:X2})");
                                }
                            }
                        }

                        i += 5 + desc.es_info_length;
                    }
                }
            }
        }

        // graceful exit support
        private static bool exit_request = false;

        private static void signal_handler(int sig)
        {
            if ((sig == SIGINT) || (sig == SIGTERM))
            {
                Console.WriteLine("- SIGINT/SIGTERM received, preparing graceful exit");
                exit_request = true;
            }
        }


        private static string GetBaseName()
        {
            return AppDomain.CurrentDomain.FriendlyName;
        }

        // main
        public static int RunMain(string[] args)
        {
            int ret = EXIT_FAILURE;

            if (args.Length > 1 && args[1] == "-V")
            {
                Console.WriteLine(TELXCC_VERSION);
                return EXIT_SUCCESS;
            }

            Console.WriteLine("telxcc - TELeteXt Closed Captions decoder");
            Console.WriteLine("(c) Forers, s. r. o., <info@forers.com>, 2011-2014; Licensed under the GPL.");
            Console.WriteLine($"Version {TELXCC_VERSION}");
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

                    return (EXIT_SUCCESS);
                }

                if (arg == "-i" && argc > argIndex + 1)
                {
                    config.input_name = args[++argIndex];
                }
                else if (arg == "-o" && argc > argIndex + 1)
                {
                    config.output_name = args[++argIndex];
                }
                else if (arg == "-p" && argc > argIndex + 1)
                {
                    config.page = Convert.ToInt32(args[++argIndex]);
                }
                else if (arg == "-t" && argc > argIndex + 1)
                {
                    config.tid = Convert.ToInt32(args[++argIndex]);
                }
                else if (arg == "-f" && argc > argIndex + 1)
                {
                    config.offset = Convert.ToInt32(args[++argIndex]);
                }
                else if (arg == "-n")
                {
                    config.bom = false;
                }
                else if (arg == "-1")
                {
                    config.nonempty = true;
                }
                else if (arg == "-c")
                {
                    config.colours = true;
                }
                else if (arg == "-v")
                {
                    config.verbose = true;
                }
                else if (arg == "-s")
                {
                    config.se_mode = true;
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
                    config.utc_refvalue = t;
                }
                else if (arg == "-m")
                {
                    config.m2ts = true;
                }
                else
                {
                    Console.WriteLine($"! Unknown option {arg}");
                    Console.WriteLine($"- For usage options run {GetBaseName()} -h");
                    return EXIT_FAILURE;
                }
                argIndex++;
            }

            if (config.m2ts)
            {
                Console.WriteLine("- Processing input stream as a BDAV MPEG-2 Transport Stream");
            }

            if (config.se_mode)
            {
                var t0 = config.utc_refvalue;
                Console.WriteLine($"- Search engine mode active, UTC referential value = {t0}");
            }

            // teletext page number out of range
            if ((config.page != 0) && ((config.page < 100) || (config.page > 899)))
            {
                Console.WriteLine("! Teletext page number could not be lower than 100 or higher than 899");
                return EXIT_FAILURE;
            }

            // default teletext page
            if (config.page > 0)
            {
                // dec to BCD, magazine pages numbers are in BCD (ETSI 300 706)
                config.page = ((config.page / 100) << 8) | (((config.page / 10) % 10) << 4) | (config.page % 10);
            }

            // PID out of range
            if (config.tid > 0x2000)
            {
                Console.WriteLine("! Transport stream PID could not be higher than 8192");
                return EXIT_FAILURE;
            }

            //signal(SIGINT, signal_handler);
            //signal(SIGTERM, signal_handler);

            if (string.IsNullOrEmpty(config.input_name) || config.input_name == "-")
            {
                Console.WriteLine($"! Please specify input file via the '-i <file name>' parameter");
                return EXIT_FAILURE;
            }
            else
            {
                try
                {
                    fin = new FileStream(config.input_name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"! Could not open input file {config.input_name}: {e.Message}");
                    return EXIT_FAILURE;
                }
            }

            if (fin.Length < 1) // isatty(fileno(fin)))
            {
                Console.WriteLine("! STDIN is a terminal. STDIN must be redirected.");
                return EXIT_FAILURE;
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
            if (config.bom)
            {
                //fprintf(fout, "\xef\xbb\xbf");
                //fflush(fout);
            }

            // PROCESING

            // FYI, packet counter
            var packet_counter = 0;

            // TS packet buffer
            var ts_packet_buffer = new byte[TS_PACKET_SIZE];
            var ts_packet_size = TS_PACKET_SIZE - 4;

            // pointer to TS packet buffer start
            byte[] ts_packet = ts_packet_buffer;

            // if telxcc is configured to be in M2TS mode, it reads larger packets and ignores first 4 bytes
            if (config.m2ts)
            {
                ts_packet_size = TS_PACKET_SIZE;
                ts_packet = new byte[ts_packet_size];
                Buffer.BlockCopy(ts_packet_buffer, 4, ts_packet, 0, ts_packet_size);
            }

            // 0xff means not set yet
            var continuity_counter = 255;

            // PES packet buffer
            byte[] payload_buffer = new byte[PAYLOAD_BUFFER_SIZE];
            var payload_counter = 0;

            // reading input
            while (!exit_request && fin.Read(ts_packet_buffer, 0, ts_packet_size) == ts_packet_size)
            {
                // not TS packet -- misaligned?
                if (ts_packet[0] != SYNC_BYTE)
                {
                    Console.WriteLine("! Invalid TS packet header; TS seems to be misaligned");

                    int shift = 0;
                    for (shift = 1; shift < TS_PACKET_SIZE; shift++) if (ts_packet[shift] == SYNC_BYTE) break;

                    if (shift < TS_PACKET_SIZE)
                    {
                        if (config.verbose) Console.WriteLine($"! TS-packet-header-like byte found shifted by {shift} bytes, aligning TS stream (at least one TS packet lost)");
                        for (var i = shift; i < TS_PACKET_SIZE; i++) ts_packet[i - shift] = ts_packet[i];
                        fin.Read(ts_packet_buffer, 0, TS_PACKET_SIZE - shift);
                    }
                }

                // Transport Stream Header
                // We do not use buffer to struct loading (e.g. ts_packet_t *header = (ts_packet_t *)ts_packet;)
                // -- struct packing is platform dependent and not performing well.
                var header = new ts_packet_t
                {
                    sync = ts_packet[0],
                    transport_error = (ts_packet[1] & 0x80) >> 7,
                    payload_unit_start = (ts_packet[1] & 0x40) >> 6,
                    transport_priority = (ts_packet[1] & 0x20) >> 5,
                    pid = ((ts_packet[1] & 0x1f) << 8) | ts_packet[2],
                    scrambling_control = (ts_packet[3] & 0xc0) >> 6,
                    adaptation_field_exists = (ts_packet[3] & 0x20) >> 5,
                    continuity_counter = ts_packet[3] & 0x0f
                };
                //uint8_t ts_payload_exists = (ts_packet[3] & 0x10) >> 4;

                var af_discontinuity = 0;
                if (header.adaptation_field_exists > 0)
                {
                    af_discontinuity = (ts_packet[5] & 0x80) >> 7;
                }

                // uncorrectable error?
                if (header.transport_error > 0)
                {
                    if (config.verbose) Console.WriteLine($"! Uncorrectable TS packet error (received CC {header.continuity_counter})");
                    continue;
                }

                // if available, calculate current PCR
                if (header.adaptation_field_exists > 0)
                {
                    // PCR in adaptation field
                    var af_pcr_exists = (ts_packet[5] & 0x10) >> 4;
                    if (af_pcr_exists > 0)
                    {
                        ulong pts = ts_packet[6];
                        pts <<= 25;
                        pts |= (ulong)((ts_packet[7] << 17));
                        pts |= (ulong)((ts_packet[8] << 9));
                        pts |= (ulong)((ts_packet[9] << 1));
                        pts |= (ulong)((ts_packet[10] >> 7));
                        global_timestamp = pts / 90;
                        pts = (ulong)(((ts_packet[10] & 0x01) << 8));
                        pts |= ts_packet[11];
                        global_timestamp += pts / 27000;
                    }
                }

                // null packet
                if (header.pid == 0x1fff) continue;

                // TID not specified, autodetect via PAT/PMT
                if (config.tid == 0)
                {
                    // process PAT
                    if (header.pid == 0x0000)
                    {
                        var patPacket = new byte[TS_PACKET_PAYLOAD_SIZE];
                        Buffer.BlockCopy(ts_packet, 4, patPacket, 0, TS_PACKET_PAYLOAD_SIZE);
                        analyze_pat(patPacket, TS_PACKET_PAYLOAD_SIZE);
                        continue;
                    }

                    // process PMT
                    if (in_array(pmt_map, pmt_map_count, header.pid) == bool_t.YES)
                    {
                        var pmtPacket = new byte[TS_PACKET_PAYLOAD_SIZE];
                        Buffer.BlockCopy(ts_packet, 4, pmtPacket, 0, TS_PACKET_PAYLOAD_SIZE);
                        analyze_pmt(pmtPacket, TS_PACKET_PAYLOAD_SIZE);
                        continue;
                    }
                }

                // TID 0x2000 specified => dummy auto detection
                if (config.tid == 0x2000)
                {
                    if (header.payload_unit_start > 0)
                    {
                        // searching for PES header and "Private Stream 1" stream_id
                        ulong pes_prefix = (ulong)((ts_packet[4] << 16) | (ts_packet[5] << 8) | ts_packet[6]);
                        var pes_stream_id = ts_packet[7];

                        if ((pes_prefix == 0x000001) && (pes_stream_id == 0xbd))
                        {
                            config.tid = header.pid;
                            Console.WriteLine($"- No teletext PID specified, first received suitable stream PID is {config.tid} ({config.tid:X2}), not guaranteed");
                            continue;
                        }
                    }
                }

                if (config.tid == header.pid)
                {
                    // TS continuity check
                    if (continuity_counter == 255) continuity_counter = header.continuity_counter;
                    else
                    {
                        if (af_discontinuity == 0)
                        {
                            continuity_counter = (continuity_counter + 1) % 16;
                            if (header.continuity_counter != continuity_counter)
                            {
                                if (config.verbose)
                                    Console.WriteLine($"- Missing TS packet, flushing pes_buffer (expected CC {continuity_counter}, received CC {header.continuity_counter}, TS discontinuity {(af_discontinuity != 0 ? "YES" : "NO")}, TS priority {(header.transport_priority != 0 ? "YES" : "NO")})");
                                payload_counter = 0;
                                continuity_counter = 255;
                            }
                        }
                    }

                    // waiting for first payload_unit_start indicator
                    if ((header.payload_unit_start == 0) && (payload_counter == 0)) continue;

                    // proceed with payload buffer
                    if ((header.payload_unit_start > 0) && (payload_counter > 0)) process_pes_packet(payload_buffer, payload_counter);

                    // new payload frame start
                    if (header.payload_unit_start > 0) payload_counter = 0;

                    // add payload data to buffer
                    if (payload_counter < (PAYLOAD_BUFFER_SIZE - TS_PACKET_PAYLOAD_SIZE))
                    {
                        Buffer.BlockCopy(ts_packet, 4, payload_buffer, payload_counter, TS_PACKET_PAYLOAD_SIZE);
                        payload_counter += TS_PACKET_PAYLOAD_SIZE;
                        packet_counter++;
                    }
                    else if (config.verbose) Console.WriteLine("! Packet payload size exceeds payload_buffer size, probably not teletext stream");
                }
            }

            // output any pending close caption
            if (page_buffer.tainted == (int)bool_t.YES)
            {
                // this time we do not subtract any frames, there will be no more frames
                page_buffer.hide_timestamp = last_timestamp;
                process_page(page_buffer);
            }

            if (config.verbose)
            {
                if (config.tid == 0)
                    Console.WriteLine($"- No teletext PID specified, no suitable PID found in PAT/PMT tables. Please specify teletext PID via -t parameter.{Environment.NewLine}  You can also specify -t 8192 for another type of autodetection (choosing the first suitable stream)");
                if (frames_produced == 0)
                    Console.WriteLine("- No frames produced. CC teletext page number was probably wrong.");
                Console.Write("- There were some CC data carried via pages = ");
                // We ignore i = 0xff, because 0xffs are teletext ending frames
                for (var i = 0; i < 255; i++)
                {
                    for (var j = 0; j < 8; j++)
                    {
                        var v = cc_map[i] & (1 << j);
                        if (v > 0) Console.Write($"{((j + 1) << 8) | i:X2} ");
                    }
                }
                Console.WriteLine();
            }

            if (!config.se_mode && frames_produced == 0 && config.nonempty)
            {
                fout.AppendLine("1");
                fout.AppendLine("00:00:00,000 --> 00:00:01,000");
                frames_produced++;
            }

            Console.WriteLine($"- Done ({packet_counter:#,###,##0} teletext packets processed, {frames_produced} frames produced)");
            Console.WriteLine("");

            if (string.IsNullOrEmpty(config.output_name) || config.output_name == "-")
            {
                Console.WriteLine(fout.ToString());
            }
            else
            {
                try
                {
                    File.WriteAllText(config.output_name, fout.ToString(), new UTF8Encoding(config.bom));
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Console.WriteLine($"! Could not write to output file {config.output_name}: {e.Message}");
                    return EXIT_FAILURE;
                }
            }

            return EXIT_SUCCESS;
        }
    }
}
