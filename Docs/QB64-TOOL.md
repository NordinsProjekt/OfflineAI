# QB64-verktyget: LLM:en kompilerar och kör QBasic-program

Agenten kan kompilera och köra QBasic-filer (.bas) från den aktiva arbetsytan med
[QB64](https://qb64.com/) — en modern QBasic-kompilator för Windows. Kompilatorfel och
programmets konsolutdata matas automatiskt tillbaka in i LLM:en via den agentiska
verktygsloopen (`AgenticChatService`), så modellen kan rätta sin egen kod och försöka igen.
Det gör verktyget särskilt användbart i Agent Mode (målagenten), som redan itererar
arbete → verifiering upp till `MaxGoalIterations` gånger.

## Konfiguration

Verktyget är avstängt tills en kompilator pekas ut. Ladda ner QB64 (eller QB64 Phoenix
Edition — samma kommandoradsflaggor) och ange sökvägen i `appsettings.json` eller user
secrets:

```json
"AppConfiguration": {
  "AgentTools": {
    "Qb64": {
      "CompilerPath": "d:/qb64/qb64.exe",
      "CompileTimeoutMs": 180000,
      "RunTimeoutMs": 30000,
      "MaxOutputLength": 4000
    }
  }
}
```

| Inställning | Standard | Betydelse |
| --- | --- | --- |
| `CompilerPath` | *(tom = avstängt)* | Full sökväg till `qb64.exe`. Kommandona erbjuds aldrig LLM:en när den är tom. |
| `CompilerArguments` | `-x "{source}" -o "{output}"` | Argumentmall. `-x` kompilerar utan att öppna IDE:t och skriver fel till konsolen. `{source}`/`{output}` ersätts med fulla sökvägar. |
| `CompileTimeoutMs` | 180000 | Kompileringstimeout. Första kompileringen på en maskin är långsammast (QB64 bygger via en medföljande C++-backend). |
| `RunTimeoutMs` | 30000 | Körtimeout — processträdet dödas när den överskrids, men utdata som hunnit fångas returneras ändå till LLM:en. |
| `MaxOutputLength` | 4000 | Max antal tecken utdata som skickas till LLM:en. Kompilatorfel trunkeras från början (felet står sist); programutdata trunkeras från slutet. |

## Kommandon som LLM:en får

- `/qb64 <fil.bas> [<<< <rad1>|<rad2>]` — kompilerar filen från agentkatalogen och kör den
  färdiga exe:n. Programmets konsolutdata (stdout/stderr + avslutningskod) skickas tillbaka
  till LLM:en. Efter `<<<` kan modellen skicka med den inmatning programmet ska få, en rad
  per `INPUT`, åtskilda med `|`.
- `/qb64-kompilera <fil.bas>` — kompilerar utan att köra. Använd för grafiska program/spel
  som inte kan köras utan användare men ändå ska syntaxkontrolleras.

### Inmatning till programmet

Utan `<<<` ser programmet filslut direkt på stdin — samma beteende som tidigare, och det som
gör att ett program med `INPUT` inte hänger för evigt. Med `<<<` skrivs raderna i stället in
på programmets stdin innan den stängs, så ett konsollprogram som faktiskt ska läsa indata kan
testas på riktigt:

```
/qb64 kalkyl.bas <<< 5|7|avsluta
```

- En rad per `INPUT` (eller per `LINE INPUT`). Raderna trimmas.
- Tom nyttolast (`/qb64 fil.bas <<<`) skickar en enda tom rad, dvs. ett blankt Enter — svaret
  ett `INPUT "Tryck retur"; a$` väntar på.
- `<<` fungerar också, eftersom modellen ofta skriver två vinkelparenteser i stället för tre.
- Räcker inte raderna hinner programmet fram till nästa `INPUT` och står kvar där tills
  körtimeouten dödar det. Timeout-meddelandet påminner då om att skicka fler rader.
- Inmatningen bara nås av konsollprogram (`$CONSOLE:ONLY`). Ett grafikprogram läser tangenter
  från sitt fönster, inte från stdin — dit kommer ingen inmatning fram.
- `/qb64-kompilera fil.bas <<< ...` avvisas: inmatning utan körning betyder ingenting.

Utdata till LLM:en innehåller raderna som skickades, så modellen kan läsa programmets svar mot
sina egna frågor: `Inmatning som skickades till programmet: "5" ⏎ "7" (2 rad(er), därefter filslut).`

### Grafikprogram kompileras, men körs aldrig

Ett program som växlar till ett grafikläge (`SCREEN 12`, `SCREEN _NEWIMAGE(...)`, ...) utan
`$CONSOLE:ONLY` ritar i ett fönster. I en huvudlös körning finns ingen som ser fönstret och
ingen som trycker på tangenten QB64 väntar på när programmet är klart — en körning kan bara
sluta med timeout. `/qb64` upptäcker därför grafikläget före körningen, kompilerar filen och
rapporterar kompileringsresultatet i stället för att lägga en hel körtimeout på ett program som
mycket väl kan vara korrekt:

```
✓ QB64: fantasy.bas kompilerade utan fel (grafikprogram — kördes inte).
```

Kontexten till LLM:en pekar ut raden och `SCREEN`-satsen som avgjorde det, och tipsar om
`/qb64-kompilera` samt om att skriva ett mätbart resultat till en textfil som kan läsas med
`/läs`. Bara `SCREEN 0` (textläge) räknas som "inget fönster"; ett läge som väljs via en
variabel antas vara grafik, eftersom det felet bara kostar en utebliven körning.

Bakgrunden är en verklig körning: ett korrekt 640×480-program kompilerades, kördes, dödades
efter 30 sekunder och underkändes sedan på kravet "programmet fungerar när det körs".

LLM:en anger bara ett filnamn — det löses alltid inne i den aktiva arbetsytan med samma
skydd mot path-traversal som filagenten, och den producerade exe:n hamnar bredvid
.bas-filen. Sökvägen till kompilatorn kommer enbart från konfigurationen; LLM:en kan aldrig
peka ut en egen körbar fil (samma vitlistningsprincip som `ExternalTools`).

## Typiskt flöde

1. Användaren: *"Skriv ett program som räknar ut de 10 första primtalen och kör det."*
2. LLM: `/fyll primtal.bas ...` → filagenten sparar koden.
3. LLM: `/qb64 primtal.bas` → kompilering misslyckas → kompilatorfelet matas tillbaka.
4. LLM: `/redigera primtal.bas ...` → rättar felet.
5. LLM: `/qb64 primtal.bas` → programmet körs, utdata matas tillbaka.
6. LLM svarar användaren med resultatet.

Ska programmet rita något: se [QBasic-grafikskillen](QBASIC-GRAFIK-SKILL.md), som låter LLM:en slå
upp rätt grafiksyntax med `/qbasic-grafik <ämne>` innan den skriver filen — och som förklarar varför
grafikprogram ska verifieras med `/qb64-kompilera` och utan `$CONSOLE:ONLY`.

Obs: i vanlig chatt begränsas antalet verktygsvarv av `AgentTools:MaxToolCallRounds`
(standard 3). För kod-kompilera-rätta-loopar är det snålt — höj gärna till 6–8. I Agent
Mode gäller i stället `MaxGoalIterations` (standard 20).

## Regler programmet måste följa (ligger i verktygsbeskrivningen till LLM:en)

- **`$CONSOLE:ONLY` måste stå först i .bas-filen.** Utan den öppnar QB64-program ett
  grafikfönster: `PRINT` går dit i stället för till stdout, inget kan fångas, och när
  programmet når `END` väntar det på en tangenttryckning tills timeouten dödar det.
- **Ingen väntan på användare**: `SLEEP` utan argument, `_KEYHIT`-loopar osv. får programmet
  att hänga tills timeouten slår till. `INPUT` är däremot användbart så länge svaren skickas
  med `<<<`; utan `<<<` ser programmet filslut direkt.
- Programmet ska avsluta sig självt (`END`/`SYSTEM`).

Timeout-, fel- och "ingen utdata"-meddelandena som skickas tillbaka till LLM:en påminner om
`$CONSOLE:ONLY`-regeln, så modellen kan självkorrigera även när den glömt den.

## Implementation

- `AgentKit/Skills/Qb64/Qb64ToolService.cs` (+ `IQb64ToolService`) — kommandotolkning
  (inklusive `<<<`-uppdelningen), arbetsyte-säker filupplösning, kompilering (arbetskatalog =
  QB64:s installationskatalog, som krävs för att den ska hitta sin C++-backend), körning
  (arbetskatalog = arbetsytan, inmatning skriven på stdin som sedan stängs) och
  timeout-dödning av hela processträdet.
- `AgentKit/Skills/Qb64/QBasicProgramKind.cs` — heuristiken bakom grafikregeln:
  `$CONSOLE:ONLY`-detektering och första `SCREEN`-satsen i ett grafikläge.
- `Services/Configuration/AppConfiguration.cs` — `Qb64Settings` under `AgentToolsSettings`.
- `AgenticChatService` — erbjuder kommandona i verktygslistan och kör dem i samma
  detektera → exekvera → mata-tillbaka-loop som övriga verktyg.
- Tester: `AgentKit.Tests/Skills/Qb64/Qb64ToolServiceTests.cs` (cmd.exe som fejk-kompilator —
  den kopierade cmd.exe:n kör dessutom det som skickas på stdin, vilket är hur inmatningen
  verifieras), `QBasicProgramKindTests.cs` och QB64-fallen i `AgenticChatServiceTests`.
