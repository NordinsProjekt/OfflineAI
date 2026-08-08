# QBasic-grafikskillen: LLM:en slår upp syntaxen innan den skriver koden

Agenten kan slå upp exakt QBasic/QB64-syntax för grafik med `/qbasic-grafik <ämne>`. Referensen är
inbyggd i `AgentKit` — inget nätverk, ingen konfiguration, inga externa filer — och är därför alltid
tillgänglig, till skillnad från [QB64-verktyget](QB64-TOOL.md) som kräver en kompilator.

## Varför

QBasic-stödet består nu av tre delar som tar problemet i tur och ordning:

| När | Vad | Var |
| --- | --- | --- |
| **Före** skrivning | LLM:en slår upp rätt syntax | `/qbasic-grafik` (den här skillen) |
| **Vid** skrivning | Strukturkontroll av .bas-filen | `QBasicStructureLinter` |
| **Efter** skrivning | Riktig kompilering och körning | `/qb64`, `/qb64-kompilera` |

Utan den första delen är loopen reaktiv: modellen gissar ett kommandonamn, får ett kompileringsfel,
gissar igen — och varje varv kostar en hel agentiteration. Den lokala 12B-modellen kan grunderna i
QBasic men minns sällan argumentordningen i `LINE ... , B`, hur stor GET-arrayen måste vara eller att
`PALETTE` tar ett hoppackat RGB-tal med komponenter i intervallet 0–63. Det är precis det referensen
svarar på.

## Kommandot

```
/qbasic-grafik <ämne>
```

Ämnena (ett kort avsnitt vardera):

| Ämne | Innehåll |
| --- | --- |
| `skärmlägen` | `SCREEN`-lägen 0/7/8/9/12/13, upplösning, färger, sidor, koordinatsystem |
| `punkter` | `PSET`, `PRESET`, `POINT`, `STEP` |
| `former` | `LINE` (inkl. `B`/`BF`), `CIRCLE`, `PAINT`, `DRAW` |
| `data` | `DATA`/`READ`/`RESTORE` — rita figurer från en färgkarta |
| `sprites` | `GET`/`PUT`, arraystorleksformeln, flera bilder i en array, action verbs |
| `sidor` | Aktiv/synlig sida, `PCOPY`, dubbelbuffring |
| `maskning` | `AND`+`OR`-masken för genomskinliga figurer |
| `palett` | `PALETTE`, `PALETTE USING`, RGB-packning |
| `fart` | `BSAVE`/`BLOAD`, DOS-knepen, och QB64:s motsvarigheter |
| `qb64` | Reglerna för grafikprogram i den här huvudlösa miljön |

Modellen kan också ange ett nyckelord (`/qbasic-grafik circle`) eller ställa en fråga
(`/qbasic-grafik hur får jag bort flimmer`) — uppslagningen matchar nyckelord och alias som hela ord,
och tål att diakriterna faller bort (`skarmlagen` hittar `skärmlägen`). Utan ämne, eller vid en miss,
returneras ämneslistan som ett **lyckat** resultat i stället för ett fel: en miss ska inte kosta ett
verktygsvarv utan att lära modellen något.

## Innehållet är anpassat efter miljön, inte efter DOS

Två saker skiljer referensen från en vanlig QBasic-handledning:

- **QB64 gäller före klassisk QBasic.** Där de skiljer sig vinner QB64, eftersom det är den
  kompilator `Qb64ToolService` startar. DOS-knep som förutsätter riktig VGA-hårdvara (palettbyte via
  `OUT &H3C8`, `POKE` till `&HA000`, `WAIT &H3DA`) står kvar men är märkta som DOS-beroende och
  parade med QB64-motsvarigheten (`_LIMIT`, `_DISPLAY`, `_PUTIMAGE`, `_MEMIMAGE`).
- **Ämnet `qb64` beskriver den här miljön.** Ett grafikprogram ska *inte* ha `$CONSOLE:ONLY` (det gör
  programmet konsollöst) och ska verifieras med `/qb64-kompilera`, inte `/qb64` — annars öppnas ett
  fönster ingen ser och körningen avbryts av timeouten, vilket ser ut som ett fel fast koden var
  korrekt. Ska programmet lämna ett mätbart resultat: skriv till en textfil i arbetsytan och läs den
  med `/läs`.

Avsnitten hålls medvetet korta. Distributionsmaskinen kör en 12B-modell med kontext 8192, där ett
avsnitt ska samsas med arbetsytans filöversikt, verktygslistan och konversationen. Ett test
(`Topics_AreShortEnoughForASmallContextWindow`) håller varje avsnitt under 2500 tecken.

## Implementation

- `AgentKit/Skills/QBasicGraphics/QBasicGraphicsReference.cs` — själva texten plus uppslagningen
  (exakt nyckel/alias först, annars poängsättning där en specifik träff slår en generisk).
- `AgentKit/Skills/QBasicGraphics/QBasicGraphicsService.cs` (+ `IQBasicGraphicsService`) —
  kommandotolkning i samma form som övriga skills, så `AgenticChatService` kan köra den genom samma
  prime → detect → execute → feed-back-loop. Tillståndslös: en instans delas av alla arbetsytor och
  jobb.
- `AgenticChatService` — ny valfri konstruktorparameter `qbasicGraphics`. Registrerad i både
  `AiDashboard` (chatt, batch, Agent Mode) och `AgentKit.Api` (huvudlösa jobb).
- `QBasicStructureLinter` — nyckelordslistan utökad med grafik- och minnessatserna referensen
  dokumenterar (`GET`, `PUT`, `BSAVE`, `BLOAD`, `PEEK`, `POKE`, `OUT`, `INP`, `WAIT`, `VARSEG`,
  `VARPTR`, `CLEAR`, `PMAP`), så ett påhittat `_GET`/`_PUT` fångas på samma sätt som `_LINE`.
- Tester: `AgentKit.Tests/Skills/QBasicGraphics/` (uppslagning, kommandotolkning, innehållsgarantier)
  och de nya fallen i `AgenticChatServiceTests`.

Ett av testerna är värt att nämna: `Topics_TeachNoSyntaxTheStructureLinterRejects` kör
`QBasicStructureLinter` över varje avsnitt och underkänner texten om den själv innehåller ett
påhittat nyckelord. En referens som lär ut felet den finns till för att förhindra vore värre än
ingen referens alls — och testet fångade faktiskt ett sådant fall när skillen skrevs.

## Källa

Ämnesindelningen följer Lucas K. Tavares grafikhandledning på Pete's QBasic Site
(<http://www.petesqbsite.com/sections/tutorials/tuts/tavares_graphics.txt>), som täcker just den väg
en nybörjare behöver: skärmläge → pixlar → former → DATA-figurer → GET/PUT → sidor → maskning →
palett → fart. Texten i referensen är skriven för det här projektet, med syntaxen kontrollerad mot
QB64 och kompletterad med de QB64-specifika delarna handledningen inte kunde känna till.
