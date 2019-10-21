using System;
using System.IO;
using System.Text;
using TelxCCSharp;
using Xunit;

namespace UnitTests
{
    public class ReadTestNormal
    {
        [Fact]
        public void Test1()
        {
            var inFileName = "./Files/NOVA_PID-121_PAGE-888.ts";
            var outFileName = Guid.NewGuid().ToString() + ".srt";
            var returnValue = TelxCC.RunMain(new[] { "-p", "888", "-i", inFileName, "-o", outFileName });
            Assert.Equal(0, returnValue);
            var srt = File.ReadAllText(outFileName, Encoding.UTF8);
            Assert.Equal(@"1
00:00:00,040 --> 00:00:04,760
-Chci tu pracovat.
-Pro vás je lepší volná noha.

2
00:00:04,880 --> 00:00:08,960
Doneste mi ještě nějaký fotky,
možná je koupím.

3
00:00:09,080 --> 00:00:11,640
Ale nemám žádný flek.

4
00:00:12,000 --> 00:00:17,200
Koukejte něco slušnýho nafotit.
Noste další fotky.

5
00:00:18,120 --> 00:00:22,600
-Dobrý den. -Dobrý.
-Pan Jameson mě posílá.

6
00:00:23,520 --> 00:00:28,560
-Vítejte v Daily Bugle.
-Díky. Jsem Peter Parker.

7
00:00:30,760 --> 00:00:33,320
Jsem fotograf.

8
00:00:33,520 --> 00:00:36,160
Jo, to vidím.

9
00:00:43,840 --> 00:00:49,080
Dnešním dnem Oscorp
porazila konkurenční Quest...

10
00:00:49,200 --> 00:00:53,520
...a stává se hlavním
dodavatelem armády USA.

11
00:00:53,640 --> 00:00:56,680
Stručně řečeno,
dámy a pánové:

12
00:00:56,800 --> 00:01:02,760
Nízké náklady, vysoké zisky.
Naše akcie nikdy nebyly výše.

13
00:01:03,040 --> 00:01:08,840
-Dobré zprávy. -Skvělé.
-Právě proto firmu prodáváme. -Co?

14
00:01:08,960 --> 00:01:13,760
Kvůli té nehodě Quest provádí
transformaci a expanduje.

15
00:01:13,880 --> 00:01:17,200
Předložili nabídku,
kterou nelze odmítnout.

16
00:01:17,320 --> 00:01:19,320
Proč nic nevím?

17
00:01:19,440 --> 00:01:22,960
Boj v managementu
by byl absolutně nežádoucí.

18
00:01:23,080 --> 00:01:28,520
Ta věc je vyřízená.
Rada očekává rezignaci do 30 dnů.

19
00:01:28,800 --> 00:01:32,000
To mi přece
nemůžete udělat.

20
00:01:33,440 --> 00:01:38,760
Založil jsem tuhle společnost.
Víte, co jsem obětoval?", srt.TrimEnd());
        }
    }
}
