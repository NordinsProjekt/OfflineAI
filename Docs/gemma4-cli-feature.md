# Gemma 4 CLI-backend

Lokal inferens via `llama-cli`-subprocess utan nätverksberoende.  
Fungerar parallellt med den klassiska poolade backend och kan väljas per session direkt i QuickAsk-gränssnittet.

---

## Innehåll

- [Översikt](#översikt)
- [Arkitektur](#arkitektur)
- [Filer](#filer)
- [Konfiguration](#konfiguration)
- [Dependency Injection](#dependency-injection)
- [Backend-växlare i UI](#backend-växlare-i-ui)
- [API-referens](#api-referens)
  - [IGemma4CliService](#iggemma4cliservice)
  - [DashboardState – Gemma 4-specifika members](#dashboardstate--gemma-4-specifika-members)
  - [LlmBackend enum](#llmbackend-enum)
- [Samplingsparametrar](#samplingsparametrar)
- [Tool-calling](#tool-calling)
- [Begränsningar](#begränsningar)

---

## Översikt

Gemma 4 CLI-backendet kör modellen som en vanlig `llama-cli`-process per anrop. Varje anrop:

1. Bygger ett Gemma 4-chattmall-prompt (`<start_of_turn>user … <start_of_turn>model`).
2. Skriver prompten till en temporär fil och startar `llama-cli -f <fil>`.
3. Samlar in `stdout` med en paus-timeout-mekanism (identisk med `PersistentLlmProcess`).
4. Extraherar svaret efter det sista `<start_of_turn>model`-markeret.

Multimodalt stöd (bilder) sker via `--image <sökväg>` som llama.cpp stöder för alla Gemma 4-storlekar.

---

## Arkitektur

```
AppConfiguration.Gemma4Cli
        │
        ▼  (Program.cs, villkorlig registrering)
IGemma4CliService  ──►  Gemma4CliService
        │                      │
        │                      ├─ ChatAsync
        │                      ├─ ChatWithImageAsync
        │                      ├─ ChatWithImageBytesAsync
        │                      └─ ChatWithToolsAsync  ──►  IAgentToolRegistry
        │
        ▼
DashboardState.Gemma4CliService
DashboardState.SelectedBackend  (LlmBackend.Classic | Gemma4Cli)
        │
        ├─ SendActiveAsync          ─► Home.razor.cs
        └─ SendQuickAskActiveAsync  ─► QuickAskPage.razor
```

---

## Filer

| Fil | Syfte |
|-----|-------|
| `AI/Gemma4/IGemma4CliService.cs` | Publik kontrakt för CLI-tjänsten |
| `AI/Gemma4/Gemma4CliService.cs` | Konkret implementation (subprocess + prompt-builder) |
| `AI/Gemma4/Gemma4CliOptions.cs` | Konfigurationsobjekt som skickas till konstruktorn |
| `AiDashboard/State/LlmBackend.cs` | Enum `Classic` / `Gemma4Cli` |
| `Services/Configuration/AppConfiguration.cs` | `Gemma4CliSettings`-sektion i appkonfigurationen |
| `AiDashboard/Program.cs` | Villkorlig DI-registrering av `IGemma4CliService` |
| `AiDashboard/State/DashboardState.cs` | Properties och dispatch-metoder för Gemma 4 |
| `AiDashboard/Components/Pages/QuickAskPage.razor` | Backend-dropdown i QuickAsk-UI |
| `AiDashboard/Components/Pages/Home.razor.cs` | Anrop via `SendActiveAsync` |

---

## Konfiguration

Lägg till sektionen `Gemma4Cli` i `appsettings.json` eller i **User Secrets** (rekommenderat för lokala sökvägar).

```json
{
  "AppConfiguration": {
    "Gemma4Cli": {
      "ModelPath":             "D:\\models\\gemma-4-26B-A4B-it-Q4_K_M.gguf",
      "LlamaCliPath":          "D:\\llama.cpp\\llama-cli.exe",
      "GpuLayers":             35,
      "ContextSize":           8192,
      "MaxTokens":             2048,
      "Temperature":           0.7,
      "TopP":                  0.9,
      "TopK":                  40,
      "TimeoutMs":             120000,
      "MaxToolCallIterations": 3
    }
  }
}
```

### Fält

| Fält | Typ | Standard | Beskrivning |
|------|-----|---------|-------------|
| `ModelPath` | `string` | `""` | **Obligatorisk.** Sökväg till Gemma 4 GGUF-filen. Tom sträng inaktiverar tjänsten. |
| `LlamaCliPath` | `string` | `""` | Sökväg till `llama-cli.exe`. Tomt = faller tillbaka på `AppConfiguration:Llm:ExecutablePath`. |
| `GpuLayers` | `int` | `0` | Antal lager att offloada till GPU (`-ngl`). Sätt `99` för full GPU-offload. |
| `ContextSize` | `int` | `4096` | KV-cache storlek i tokens (`-c`). Gemma 4 26B A4B stöder upp till 256 K. |
| `MaxTokens` | `int` | `2048` | Max antal genererade tokens per anrop (`-n`). |
| `Temperature` | `float` | `0.7` | Samplingstemperatur. Gemma 4-dokumentationen rekommenderar `1.0`. |
| `TopP` | `float` | `0.9` | Nucleus sampling. Gemma 4-dokumentationen rekommenderar `0.95`. |
| `TopK` | `int` | `40` | Top-K sampling. Gemma 4-dokumentationen rekommenderar `64`. |
| `TimeoutMs` | `int` | `120000` | Hård timeout per subprocess-anrop (ms). |
| `MaxToolCallIterations` | `int` | `3` | Max antal tool-call/tool-result rundturer innan tvingat slutsvar. |

> **Minsta konfiguration:** Ange bara `ModelPath` om `llama-cli.exe` redan finns konfigurerat under `AppConfiguration:Llm:ExecutablePath`.

---

## Dependency Injection

Tjänsten registreras **villkorligt** i `Program.cs` — om `ModelPath` är tomt hoppar registreringen över och ett varningsmeddelande skrivs till konsolen:

```
[+] Gemma 4 CLI service registered (model: gemma-4-26B-A4B-it-Q4_K_M.gguf)
[+] Gemma 4 CLI service attached to dashboard
```

eller

```
[!] Gemma 4 CLI service not configured (AppConfiguration:Gemma4Cli:ModelPath missing)
```

Tjänsterna som registreras:

```csharp
// Alltid registrerad (används även av framtida tool-calling i andra tjänster)
builder.Services.AddSingleton<IAgentToolRegistry, AgentToolRegistry>();

// Villkorlig – kräver ModelPath + LlamaCliPath (eller Llm:ExecutablePath)
builder.Services.AddSingleton<IGemma4CliService>(sp => new Gemma4CliService(opts));
```

`IGemma4CliService` kopplas sedan till `DashboardState.Gemma4CliService` i state-factory-blocket.

---

## Backend-växlare i UI

### QuickAsk (`/quick-ask`)

En **Backend**-dropdown visas i info-raden *enbart* när `Dashboard.IsGemma4Available` är `true`:

```
[ Model: gemma-2-2b ] [ Tokens: 128K ] [ Backend: Classic ▼ ]
```

Alternativ i dropdown: **Classic** | **Gemma 4 CLI**

Valet sparas i `DashboardState.SelectedBackend` och är aktivt för alla efterföljande anrop tills sidan laddas om eller valet ändras.

### Chat-dashboard (`/chat`)

Använder `Dashboard.SendActiveAsync(message)` som routar automatiskt baserat på `SelectedBackend`. Ingen separat UI-kontroll – ändra backend via QuickAsk eller programmatiskt.

---

## API-referens

### `IGemma4CliService`

```csharp
// Enkel text-chatt
Task<string> ChatAsync(string userMessage, CancellationToken ct = default);

// Multimodal – bildfil på disk
Task<string> ChatWithImageAsync(
    string userMessage,
    string imagePath,
    CancellationToken ct = default);

// Multimodal – råa bildbytes (skrivs till tempfil, rensas efteråt)
Task<string> ChatWithImageBytesAsync(
    string userMessage,
    ReadOnlyMemory<byte> imageData,
    string mimeType = "image/jpeg",
    CancellationToken ct = default);

// Tool-calling med automatisk runtur-loop
Task<string> ChatWithToolsAsync(
    string userMessage,
    IAgentToolRegistry toolRegistry,
    CancellationToken ct = default);
```

### `DashboardState` – Gemma 4-specifika members

| Member | Typ | Beskrivning |
|--------|-----|-------------|
| `Gemma4CliService` | `IGemma4CliService?` | Sätts av `Program.cs`. `null` när tjänsten inte är konfigurerad. |
| `IsGemma4Available` | `bool` | `true` om `Gemma4CliService != null`. |
| `SelectedBackend` | `LlmBackend` | Aktiv backend. Default: `Classic`. |
| `SendActiveAsync(string)` | `Task<string>` | Routar till Gemma 4 CLI eller klassisk backend beroende på `SelectedBackend`. Används av Home-sidan. |
| `SendQuickAskActiveAsync(string)` | `Task<string>` | Som `SendActiveAsync` men utan RAG-historik. Används av QuickAsk. |

### `LlmBackend` enum

```csharp
public enum LlmBackend
{
    /// Klassisk poolad subprocess (PersistentLlmProcess) med valfri RAG.
    Classic,

    /// Lokal Gemma 4 subprocess via llama-cli (offline, ingen RAG).
    Gemma4Cli
}
```

---

## Samplingsparametrar

Gemma 4-teamets rekommenderade värden skiljer sig från OfflineAI:s generella standarder:

| Parameter | OfflineAI-standard | Gemma 4-rekommendation |
|-----------|-------------------|------------------------|
| Temperature | 0.3 | 1.0 |
| Top-P | 0.85 | 0.95 |
| Top-K | 30 | 64 |

`Gemma4CliOptions` använder Gemma 4-rekommendationerna som inbyggda defaults.  
`Gemma4CliSettings` (JSON-konfiguration) använder något lägre defaults som kompromiss – justera efter behov.

---

## Tool-calling

`ChatWithToolsAsync` implementerar en JSON-baserad runtur-loop:

1. Tool-definitioner från `IAgentToolRegistry` serialiseras och injiceras i user-prompten.
2. Modellen returnerar ett JSON-array med tool-call(s).
3. Varje tool anropas mot registret och resultaten bifogas som ett `<start_of_turn>tool`-turn.
4. Modellen anropas igen – detta upprepas tills modellen returnerar ren text eller `MaxToolCallIterations` nås.

Inbyggda filverktyg (`create_file`, `read_file`, `write_file`, `list_files`) finns i `Services/AgentTools/BuiltInFileTools.cs` och kan registreras i `IAgentToolRegistry`.

> **Obs:** Detta är den JSON/Semantic-Kernel-baserade tool-calling-mekanismen. QuickAsk och
> Dashboard-chatten använder istället en enklare, textbaserad variant
> (`IAgenticChatService`) som fungerar med vilken backend som helst — se
> [AGENTIC-CHAT-TOOL-CALLING.md](AGENTIC-CHAT-TOOL-CALLING.md).

---

## Begränsningar

| Begränsning | Detalj |
|-------------|--------|
| Ingen RAG | Gemma 4 CLI-backendet har inte tillgång till vektordatabasen. Använd Classic-backendet för RAG-frågor. |
| Chat-historik | Varje anrop är stateless. Historikkontext (konversationsminne) skickas inte automatiskt. |
| Chattemall | Prompt-byggaren är specifik för Gemma 4-formatet (`<start_of_turn>` / `<end_of_turn>`). Andra modellers GGUF-filer fungerar inte utan att bygga-utöka `BuildGemma4Prompt`. |
| Processisolering | Varje anrop startar en ny process. Ingen varm GPU-cache mellan anrop — första token kan vara långsam. |
| Timeout | Hård timeout via `TimeoutMs`. Öka värdet för stora modeller eller långa svar. |
