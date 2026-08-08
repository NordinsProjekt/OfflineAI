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

- `/qb64 <fil.bas>` — kompilerar filen från agentkatalogen och kör den färdiga exe:n.
  Programmets konsolutdata (stdout/stderr + avslutningskod) skickas tillbaka till LLM:en.
- `/qb64-kompilera <fil.bas>` — kompilerar utan att köra. Använd för grafiska program/spel
  som inte kan köras utan användare men ändå ska syntaxkontrolleras.

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
- **Ingen väntan på användare**: `INPUT`, `SLEEP` utan argument, `_KEYHIT`-loopar osv. får
  programmet att hänga tills timeouten slår till. Programmets stdin är stängd (EOF) —
  det körs utan människa vid tangentbordet.
- Programmet ska avsluta sig självt (`END`/`SYSTEM`).

Timeout-, fel- och "ingen utdata"-meddelandena som skickas tillbaka till LLM:en påminner om
`$CONSOLE:ONLY`-regeln, så modellen kan självkorrigera även när den glömt den.

## Implementation

- `Services/AgentTools/Qb64ToolService.cs` (+ `IQb64ToolService`) — kommandotolkning,
  arbetsyte-säker filupplösning, kompilering (arbetskatalog = QB64:s installationskatalog,
  som krävs för att den ska hitta sin C++-backend), körning (arbetskatalog = arbetsytan,
  stdin stängd) och timeout-dödning av hela processträdet.
- `Services/Configuration/AppConfiguration.cs` — `Qb64Settings` under `AgentToolsSettings`.
- `AgenticChatService` — erbjuder kommandona i verktygslistan och kör dem i samma
  detektera → exekvera → mata-tillbaka-loop som övriga verktyg.
- Tester: `Services.Tests/AgentTools/Qb64ToolServiceTests.cs` (cmd.exe som fejk-kompilator)
  och QB64-fallen i `AgenticChatServiceTests`.
