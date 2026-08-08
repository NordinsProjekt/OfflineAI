namespace AgentKit.Skills.QBasicGraphics;

/// <summary>
/// One look-up-able article in <see cref="QBasicGraphicsReference"/>.
/// </summary>
/// <param name="Key">Canonical topic name, used as the argument to the slash command.</param>
/// <param name="Summary">One-line description shown in the topic index.</param>
/// <param name="Aliases">
/// Extra words that select this topic. Includes the QBasic keywords the topic documents, so a
/// model that already knows it wants <c>CIRCLE</c> (but not the surrounding syntax) finds the
/// article by naming the keyword. Matched as whole words, diacritics folded.
/// </param>
/// <param name="Body">The article itself — what gets fed back into the model's context.</param>
public sealed record QBasicGraphicsTopic(
    string Key,
    string Summary,
    IReadOnlyList<string> Aliases,
    string Body);

/// <summary>
/// A compact, offline QBasic/QB64 graphics reference the LLM can look topics up in before it
/// writes a .bas file, instead of guessing at syntax it half-remembers.
/// <para>
/// This exists because of a specific, repeated failure mode: a local model asked for a graphical
/// QBasic program produces plausible-looking code with invented keywords (<c>_LINE</c>,
/// <c>_KEYHIT$</c>) or misremembered argument order, and only finds out one slow compile at a
/// time. <see cref="Qb64.QBasicStructureLinter"/> catches such code after it is written; this
/// reference is the other half — giving the model the right syntax before it writes anything.
/// </para>
/// <para>
/// Content is deliberately terse. The deployment target runs a 12B model at a context of 8192
/// tokens, so one article has to fit in the same context as the workspace snapshot, the tool list
/// and the conversation — an exhaustive language manual would be unusable here. Each article is
/// therefore a page of syntax lines plus the mistakes that actually cost iterations, and points
/// at neighbouring topics rather than repeating them.
/// </para>
/// <para>
/// Where classic QBasic and QB64 differ, the QB64 behaviour wins: QB64 is the compiler
/// <see cref="Qb64.Qb64ToolService"/> invokes, so DOS-era techniques that depend on real VGA
/// hardware (direct palette ports, POKE to &amp;HA000, WAIT on the retrace port) are labelled as
/// such and paired with the QB64 replacement.
/// </para>
/// </summary>
public static class QBasicGraphicsReference
{
    /// <summary>Every article, in reading order (roughly simplest first).</summary>
    public static IReadOnlyList<QBasicGraphicsTopic> Topics { get; } = BuildTopics();

    /// <summary>
    /// Finds the article that best matches free-text <paramref name="query"/>, or <c>null</c> when
    /// nothing matches. An exact key/alias hit wins outright; otherwise every alias occurring as a
    /// whole word in the query scores its own length, so a specific term ("dubbelbuffring") beats a
    /// generic one ("rita") that several articles share.
    /// </summary>
    public static QBasicGraphicsTopic? Find(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var normalized = Fold(query.Trim());

        var exact = Topics.FirstOrDefault(topic =>
            Fold(topic.Key) == normalized || topic.Aliases.Any(alias => Fold(alias) == normalized));
        if (exact is not null)
            return exact;

        QBasicGraphicsTopic? best = null;
        var bestScore = 0;

        foreach (var topic in Topics)
        {
            var score = ScoreTopic(topic, normalized);
            if (score > bestScore)
            {
                best = topic;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// The topic list as an LLM-facing menu: one line per article. Returned whenever the command
    /// arrives without an argument or with something no article matches, so a miss still leaves the
    /// model knowing exactly which words work.
    /// </summary>
    public static string BuildIndex()
    {
        var lines = Topics.Select(topic => $"- {topic.Key}: {topic.Summary}");
        return "QBasic-grafik — tillgängliga ämnen (skriv /qbasic-grafik <ämne>):\n"
             + string.Join("\n", lines);
    }

    private static int ScoreTopic(QBasicGraphicsTopic topic, string foldedQuery)
    {
        var score = 0;
        foreach (var alias in topic.Aliases)
        {
            var folded = Fold(alias);
            if (ContainsWord(foldedQuery, folded))
                score += folded.Length;
        }
        return score;
    }

    /// <summary>
    /// True when <paramref name="needle"/> occurs in <paramref name="haystack"/> bounded by
    /// non-alphanumerics on both sides. Whole-word matching is what keeps the short aliases usable:
    /// a substring match would let "or" (a masking action verb) fire on the word "format".
    /// </summary>
    private static bool ContainsWord(string haystack, string needle)
    {
        if (needle.Length == 0)
            return false;

        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !IsWordChar(haystack[index - 1]);
            var afterIndex = index + needle.Length;
            var afterOk = afterIndex >= haystack.Length || !IsWordChar(haystack[afterIndex]);

            if (beforeOk && afterOk)
                return true;

            index = haystack.IndexOf(needle, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Lower-cases and folds Swedish diacritics (å/ä → a, ö → o) so a model that types
    /// "skarmlagen" — or a client that mangles the encoding — still reaches "skärmlägen".
    /// </summary>
    private static string Fold(string text)
    {
        var lowered = text.ToLowerInvariant();
        var sb = new System.Text.StringBuilder(lowered.Length);
        foreach (var c in lowered)
        {
            sb.Append(c switch
            {
                'å' or 'ä' => 'a',
                'ö' => 'o',
                'é' or 'è' => 'e',
                _ => c
            });
        }
        return sb.ToString();
    }

    private static List<QBasicGraphicsTopic> BuildTopics() =>
    [
        new("skärmlägen",
            "SCREEN-lägen, upplösning, antal färger och koordinatsystemet.",
            ["skärmläge", "skarmlage", "screen", "grafikläge", "upplösning", "läge", "lägen", "mode", "cls", "color", "starta"],
            """
            SCREEN-lägen (grafikläge, upplösning och färger)

            Syntax: SCREEN läge[, färgväxling][, aktivsida][, synligsida]

              Läge  Upplösning  Färger  Sidor  Kommentar
              0     text          16    flera  textläge — ingen grafik
              7     320x200       16    8      flest sidor, bra för animation utan flimmer
              8     640x200       16    4
              9     640x350       16    2
              12    640x480       16    1      högsta upplösningen i klassisk QBasic
              13    320x200      256    1      vanligast: enklast pixelhantering och flest färger

            Koordinater skrivs (x, y) med (0, 0) i övre vänstra hörnet: x växer åt höger, y nedåt.
            Sista pixeln i SCREEN 13 är alltså (319, 199). Att rita utanför skärmen är inte ett fel,
            men syns inte.

            CLS rensar skärmen. COLOR förgrund[, bakgrund] sätter färgen som PSET, LINE och PRINT
            använder när färgargumentet utelämnas.

            QB64: SCREEN _NEWIMAGE(bredd, höjd, 32) ger valfri upplösning och 32-bitars färg
            (_RGB32(r, g, b) med 0-255 per kanal) i stället för en palett. De klassiska lägena ovan
            fungerar också och är ofta enklare.

            Se även: punkter, former, sidor, palett
            """),

        new("punkter",
            "PSET, PRESET, POINT och STEP — enskilda pixlar.",
            ["punkt", "pixel", "pixlar", "prick", "pset", "preset", "point", "step", "kollision"],
            """
            Enskilda pixlar: PSET, PRESET, POINT

            PSET [STEP](x, y)[, färg]    ritar en pixel; utan färg används aktuell förgrundsfärg
            PRESET [STEP](x, y)[, färg]  samma sak, men utan färg används bakgrundsfärgen (raderar)
            POINT(x, y)                  FUNKTION: returnerar färgnumret i pixeln

            STEP gör koordinaten relativ till den senast ritade punkten:
              PSET (10, 10)     ' absolut -> pixel (10,10)
              PSET STEP(5, 5)   ' relativt -> pixel (15,15)
            STEP fungerar likadant i LINE, CIRCLE, PAINT, GET och PUT.

            POINT är en funktion, så den används i ett uttryck — enkel kollisionskontroll och
            maskbygge är de vanligaste användningarna:
              IF POINT(x, y) <> 0 THEN träff = 1
            POINT(0) till POINT(3) returnerar i stället grafikmarkörens koordinater; behövs sällan.

            Att rita pixel för pixel i en loop är långsamt. Rita figuren en gång, lagra den med GET
            och rita ut den med PUT (se: sprites).

            Se även: former, sprites, maskning
            """),

        new("former",
            "LINE, CIRCLE, PAINT och DRAW — linjer, rutor, cirklar och ifyllnad.",
            ["form", "former", "linje", "linjer", "line", "circle", "cirkel", "ellips", "båge", "rektangel", "ruta", "box", "paint", "fyll", "draw", "rita"],
            """
            Linjer, rektanglar, cirklar och ifyllnad

            LINE [[STEP](x1,y1)]-[STEP](x2,y2)[, färg][, B|BF][, stil]
              utan B/BF: en linje mellan punkterna
              B  = rektangel (bara ram), BF = fylld rektangel
              stil = 16-bitars mönster för streckade linjer (t.ex. &HFF00)
              utelämnas startpunkten fortsätter linjen från den senast ritade punkten
              LINE (0,0)-(319,199), 4        ' linje i färg 4
              LINE (10,10)-(60,40), 15, B    ' ram
              LINE (10,10)-(60,40), 2, BF    ' fylld ruta

            CIRCLE [STEP](x,y), radie[, färg][, start, slut][, aspekt]
              start/slut anges i RADIANER och ritar en båge i stället för hel cirkel
              negativa vinklar drar dessutom en radie in till mitten (tårtbit)
              aspekt < 1 ger en bredare ellips, > 1 en högre
              CIRCLE (160,100), 50, 14
              CIRCLE (160,100), 50, 14, 0, 3.14159    ' halvcirkel

            PAINT [STEP](x,y)[, fyllfärg][, kantfärg]
              fyller ytan från (x,y) tills kantfärgen nås. Ytan måste vara HELT sluten — annars
              läcker fyllningen ut över resten av skärmen.

            DRAW "sträng" ritar med en penna som håller reda på sin position:
              U/D/L/R n  upp/ner/vänster/höger n pixlar     E/F/G/H n  diagonalt
              Mx,y  flytta till    B  nästa rörelse ritar inte    N  återgå till startpunkten
              Cn  färg    An  vrid 90 grader * n    TAn  vrid n grader    Sn  skala (n/4)
              DRAW "C4 R20 D20 L20 U20"          ' kvadrat i färg 4
              DRAW "C15 BM160,100 TA45 R30"      ' linje vriden 45 grader från mitten

            Se även: punkter, data, palett
            """),

        new("data",
            "DATA/READ/RESTORE — rita figurer från en färgkarta i koden.",
            ["data", "read", "restore", "etikett", "färgkarta", "bildkarta", "rutnät", "tabell"],
            """
            Rita bilder från DATA-satser

            DATA värde[, värde]...   lagrar konstanter i programmet
            READ variabel[, ...]     läser nästa värde(n) i tur och ordning
            RESTORE [etikett]        flyttar läspositionen till en etikett (utan etikett: första DATA)

            En figur lagras som ett rutnät av färgnummer och ritas ut med två loopar:

              SCREEN 13
              RESTORE Eld
              FOR py = 0 TO 5
                FOR px = 0 TO 5
                  READ c
                  PSET (px, py), c
                NEXT px
              NEXT py
              END

              Eld:
              DATA 0, 0, 4, 4, 0, 0
              DATA 0, 4,14,14, 4, 0
              DATA 4,14,14,14,14, 4
              DATA 4, 4, 4, 4, 4, 4

            Den yttre loopen måste vara y (raderna) — byter du plats på px och py ritas figuren
            vriden 90 grader. Det kan användas med flit: FOR py = 5 TO 0 STEP -1 speglar figuren
            lodrätt, så en enda DATA-tabell kan ge alla fyra riktningarna i t.ex. ett bilspel.

            READ fortsätter alltid där förra READ slutade, oavsett vilken DATA-rad värdet står på.
            Har du flera figurer: sätt en etikett före varje DATA-block och gör RESTORE till rätt
            etikett innan varje loop. Läser du fler värden än det finns DATA kvar blir det
            "Out of DATA"-fel.

            Se även: sprites, punkter
            """),

        new("sprites",
            "GET och PUT — spara en bild i en array och rita ut den snabbt.",
            ["sprite", "get", "put", "array", "arrayen", "verb", "actionverb", "figur", "animation", "arraystorlek"],
            """
            Spara och rita bilder snabbt: GET och PUT

            GET [STEP](x1,y1)-[STEP](x2,y2), array[(index)]
            PUT [STEP](x,y), array[(index)][, verb]

            GET kopierar en rektangel från skärmen till en numerisk array; PUT ritar tillbaka den.
            Det är långt snabbare än att rita om figuren pixel för pixel.

            Arrayen måste vara stor nog. Storlek i BYTE:
              4 + INT((bredd * bitar_per_pixel + 7) / 8) * plan * höjd
            där bredd = x2-x1+1 och höjd = y2-y1+1:
              SCREEN 13:      bitar_per_pixel = 8, plan = 1  ->  4 + bredd * höjd byte
              SCREEN 7/8/12:  bitar_per_pixel = 1, plan = 4  ->  4 + INT((bredd+7)/8) * 4 * höjd byte
            Dela sedan med elementets storlek (INTEGER 2, LONG 4, SINGLE 4, DOUBLE 8) och avrunda
            UPPÅT. En 6x6-figur i SCREEN 13: 4 + 36 = 40 byte = 20 INTEGER:
              DIM eld(0 TO 19) AS INTEGER
            För liten array skriver utanför minnet och ger svårhittade fel; något för stor är ofarligt.

            Flera figurer i samma array: lägg nästa figur på index direkt efter den förra.
              GET (0,0)-(5,5), eld(0)
              GET (6,0)-(11,5), eld(20)
              PUT (20,20), eld(0), PSET
              PUT (20,20), eld(20), PSET

            Verb = hur figuren kombineras med det som redan finns på skärmen:
              PSET   ritar figuren som den är och suddar det som fanns
              PRESET ritar den med omvända färger
              AND    behåller bara det som finns i båda
              OR     lägger figuren ovanpå
              XOR    vänder pixlarna — samma PUT igen återställer bakgrunden (standard om verb utelämnas)

            QB64: för bilder skapade med _NEWIMAGE(..., 32) används _PUTIMAGE i stället. GET/PUT
            gäller de klassiska lägena.

            Se även: maskning, sidor, data
            """),

        new("sidor",
            "Aktiv/synlig sida och PCOPY — dubbelbuffring utan flimmer.",
            ["sida", "sidor", "page", "pages", "pcopy", "dubbelbuffring", "buffert", "aktivsida", "synligsida", "flimmerfri"],
            """
            Flera sidor: rita osynligt, visa när bilden är färdig

            SCREEN läge, , aktivsida, synligsida
              aktivsida  = sidan som alla ritkommandon skriver till
              synligsida = sidan användaren ser
            Stöds i SCREEN 7 (8 sidor), 8 (4 sidor), 9 (2 sidor) och 10 — men INTE i 12 eller 13.

            PCOPY källsida, målsida    kopierar en hel sida till en annan.

            Två vanliga upplägg:
              1) Växla sidor: rita på 1 medan 0 visas, byt sedan roll.
                 SCREEN 7, , 1, 0    ' rita på sida 1, visa sida 0
                 SCREEN 7, , 0, 1    ' rita på sida 0, visa sida 1
              2) Rita alltid på sida 1 och kopiera fram den när bilden är klar:
                 PCOPY 1, 0

            Poängen är att användaren aldrig ser en halvritad bild — det är den enskilt viktigaste
            åtgärden mot flimmer i animationer.

            SCREEN 13 har bara en sida. Vill du ha 256 färger OCH dubbelbuffring: använd QB64:s
            motsvarighet i stället.
              buffert& = _NEWIMAGE(320, 200, 256)
              _DEST buffert&              ' allt ritas nu till bufferten
              ' ... rita bildrutan ...
              _DEST 0                     ' tillbaka till skärmen
              _PUTIMAGE , buffert&, 0     ' kopiera fram hela bufferten
            Alternativt: låt allt ritas direkt på skärmen men styr när det visas med _DISPLAY
            (se: fart).

            Se även: fart, sprites
            """),

        new("maskning",
            "Rita en figur ovanpå en bakgrund utan fyrkantig ram (AND + OR).",
            ["mask", "maskning", "masken", "genomskinlig", "genomskinlighet", "transparent", "and", "or", "xor", "bakgrund"],
            """
            Maskning: figur ovanpå bakgrund utan fyrkantig ram

            Ett rakt PUT ritar hela rektangeln, alltså även bakgrundsfärgen runt figuren. Lösningen
            är en mask — en tvåfärgad kopia av figuren — och två PUT i rad.

            1. Rita figuren och GET:a den som vanligt (se: sprites).
            2. Bygg masken på samma yta: varje genomskinlig pixel (färg 0) blir "alla ettor",
               allt annat blir 0.
                 FOR py = 0 TO 5
                   FOR px = 0 TO 5
                     IF POINT(px, py) = 0 THEN PSET (px, py), 255 ELSE PSET (px, py), 0
                   NEXT px
                 NEXT py
                 GET (0,0)-(5,5), mask
            3. Rita ut på bakgrunden, i den här ordningen:
                 PUT (x, y), mask, AND     ' stansar ut ett hål i bakgrunden i figurens form
                 PUT (x, y), fig, OR       ' lägger figuren i hålet

            Ordningen spelar roll: AND-masken först, OR-figuren sedan. Maskens "genomskinliga"
            värde ska vara alla ettor för färgdjupet — 255 i SCREEN 13, 15 i 16-färgslägena.

            Behöver du bara låta något blinka fram och tillbaka räcker XOR: samma PUT en gång till
            på samma plats återställer bakgrunden exakt.

            Se även: sprites, punkter
            """),

        new("palett",
            "PALETTE och PALETTE USING — byta färgerna utan att rita om.",
            ["palett", "paletten", "palette", "färg", "färger", "rgb", "nyans", "toning", "fade", "blinka"],
            """
            Ändra färgerna: PALETTE

            PALETTE [attribut, färg]     utan argument återställs standardpaletten

            I SCREEN 13 (och 12) är färg ett hoppackat RGB-tal:
              färg = röd + grön * 256 + blå * 65536
            Varje komponent går från 0 till 63 — inte 0 till 255.
              PALETTE 1, 63                          ' färg 1 blir helröd
              PALETTE 2, 63 * 65536                  ' färg 2 blir helblå
              PALETTE 3, 63 + 63 * 256 + 63 * 65536  ' färg 3 blir vit

            I 16-färgslägen som SCREEN 7 går det INTE att ange RGB. Där pekar attributet bara ut en
            annan av de befintliga färgerna, och ett stort RGB-tal ger "Illegal function call":
              PALETTE 4, 1     ' allt som ritats i attribut 4 visas nu som färg 1

            PALETTE USING byter hela paletten i ett svep från en LONG-array:
              DIM pal(0 TO 255) AS LONG
              ' ... fyll pal() med hoppackade RGB-tal ...
              PALETTE USING pal(0)

            Vanliga sätt att hålla reda på paletten: en LONG-array med färdiga RGB-tal (krävs för
            PALETTE USING), en INTEGER-array pal(färg, 0 TO 2) med r/g/b var för sig, eller en egen
            TYPE med tre fält.

            Palettbyte är ett mycket billigt sätt att blinka, tona ner en bild eller byta färgtema:
            ingen pixel behöver ritas om.

            QB64: i 32-bitarslägen finns ingen palett — där anges färgen direkt i ritkommandot med
            _RGB32(r, g, b), 0-255 per kanal.

            Se även: skärmlägen, fart
            """),

        new("fart",
            "BSAVE/BLOAD, DOS-knep för hastighet, och QB64:s motsvarigheter (_LIMIT, _DISPLAY).",
            ["fart", "snabb", "snabbare", "hastighet", "prestanda", "optimera", "flimmer", "flicker", "bsave", "bload", "peek", "poke", "out", "inp", "wait", "varseg", "varptr", "def", "seg"],
            """
            Snabbare grafik och mindre flimmer

            BSAVE/BLOAD sparar en GET-array binärt till disk och läser tillbaka den snabbt:
              DEF SEG = VARSEG(bild(0))
              BSAVE "bild.bsv", VARPTR(bild(0)), antal_byte
              DEF SEG                            ' återställ segmentet — glöms det bort hamnar
              ' ... senare ...                   ' nästa POKE/BLOAD i fel minne
              DEF SEG = VARSEG(bild(0))
              BLOAD "bild.bsv", VARPTR(bild(0))
              DEF SEG
            antal_byte är samma tal som arraystorleken i sprites-avsnittet (elementens antal gånger
            elementstorleken).

            DOS-knep som förutsätter äkta VGA-hårdvara. De fungerar i original-QBasic men ska INTE
            användas i program som kompileras med QB64 på Windows:
              DEF SEG = &HA000 : POKE (y * 320&) + x, färg : DEF SEG     ' snabb PSET, bara SCREEN 13
              c = PEEK((y * 320&) + x)                                   ' snabb POINT
              OUT &H3C8, attribut : OUT &H3C9, r : OUT &H3C9, g : OUT &H3C9, b   ' snabbt palettbyte
              WAIT &H3DA, 8 : WAIT &H3DA, 8, 8                           ' synka mot bildskärmen
            (&-tecknet i 320& är avsiktligt: utan det spolar heltalsberäkningen över redan vid y > 102.)

            Motsvarigheterna i QB64 — använd dessa i stället:
              _LIMIT 60              begränsa loopen till 60 varv/sekund (och lämna CPU åt systemet)
              _DISPLAY               visa bilden först när den är färdigritad; stänger av den
                                     automatiska uppdateringen tills _AUTODISPLAY slås på igen
              _NEWIMAGE + _PUTIMAGE  dubbelbuffring i alla lägen (se: sidor)
              _SAVEIMAGE / _LOADIMAGE  spara och läsa riktiga bildfiler i stället för BSAVE/BLOAD
              _MEMIMAGE / _MEM       direkt pixelåtkomst när PSET verkligen är för långsamt

            Optimera inte i onödan: QB64 kompilerar till maskinkod och är många gånger snabbare än
            DOS-QBasic. PSET i en loop räcker längre än man tror.

            Se även: sidor, palett, qb64
            """),

        new("qb64",
            "Regler för grafikprogram som kompileras här: ingen skärm, ingen användare.",
            ["qb64", "kompilera", "kompilering", "konsol", "console", "timeout", "köra", "körning", "verifiera", "test", "understreck"],
            """
            Grafikprogram i den här miljön (QB64, ingen människa vid tangentbordet)

            - $CONSOLE:ONLY och grafik går inte ihop. Metakommandot gör programmet konsollöst, så
              ett program som kör SCREEN 13 ska inte ha det. Regeln "$CONSOLE:ONLY överst i filen"
              gäller textprogram vars utdata ska läsas tillbaka.
            - Verifiera grafikprogram med /qb64-kompilera, inte /qb64. Kompileringen kontrollerar
              koden; /qb64 skulle starta ett fönster som ingen ser och till slut avbrytas av
              körtimeouten — vilket ser ut som ett fel fast programmet var korrekt.
            - Inget får vänta på en användare: INPUT, SLEEP utan argument och loopar som väntar på
              INKEY$ eller _KEYHIT hänger tills timeouten dödar programmet.
            - Behöver programmet lämna ett bevis på att det räknat rätt: skriv till en textfil i
              arbetsytan i stället för till skärmen, och läs den efteråt med /läs.
                OPEN "resultat.txt" FOR OUTPUT AS #1
                PRINT #1, "poäng:"; poäng
                CLOSE #1
            - Ska en animation ändå kunna köras klart: räkna bildrutor och avsluta själv
              (FOR-loop + _LIMIT 30 + END), aldrig DO...LOOP utan avbrottsvillkor.
            - Klassiska nyckelord skrivs UTAN inledande understreck: LINE, CIRCLE, PSET, PAINT,
              PALETTE, GET, PUT, INKEY$. Sätter du understreck framför ett av dem finns nyckelordet
              inte alls och kompileringen stannar på den raden. Understrecket är reserverat för
              QB64:s egna tillägg: _RGB32, _NEWIMAGE, _LIMIT, _DISPLAY, _PUTIMAGE, _KEYHIT.
            - All körbar huvudkod ska ligga före den första SUB/FUNCTION i filen, annars vägrar
              QB64 kompilera ("Statement cannot be placed between SUB/FUNCTIONs").

            Se även: skärmlägen, fart
            """)
    ];
}
