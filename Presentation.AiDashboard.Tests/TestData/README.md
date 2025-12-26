# Kursplan Test Data - Svenska Utbildningsdokument

## Översikt
Denna mapp innehåller testdata för att validera dokumentanalys och kursplansextraktion med fokus på svenska utbildningsdokument.

## Testfiler

### 1. sample-kursplan.txt
**Typ**: YH-Kursplan (Yrkeshögskola)  
**Ämne**: Databaser och databasdesign  
**Poäng**: 35 yhp  
**Innehåll**:
- #Kunskaper (10 mål)
- #Färdigheter (6 mål)
- #Kompetenser (5 mål)

**Användning**: 
- Testa grundläggande kursplansextraktion
- Validera numrering över sektioner (K01-K10, F11-F16, Ko17-Ko21)
- Verifiera svenska tecken (åäö)

**Förväntat resultat**:
```
## Databaser och databasdesign
 *(35 yhp)*

# Kunskaper
**K01** - Databassystems funktioner och uppbyggnad
**K02** - Relationsdatabaser som SQL Server
...

# Färdigheter  
**F11** - Skriva korrekta förfrågningar med SQL
...

# Kompetenser
**Ko17** - Skapa relationsdatabasmodeller
...
```

---

### 2. sample-yh-kursplan.txt
**Typ**: YH-Kursplan  
**Ämne**: Webbutveckling  
**Poäng**: 40 yhp  
**Innehåll**:
- #Kunskaper (8 mål)
- #Färdigheter (8 mål)
- #Kompetenser (7 mål)

**Användning**:
- Testa längre kursplaner
- Validera olika ämnesområden
- Testa mer komplexa kompetensbeskrivningar

---

### 3. csharp-kursplan.txt
**Typ**: YH-Kursplan med metadata  
**Ämne**: Programmering i C#  
**Poäng**: 50 yhp  
**Extra metadata**:
- Kursnummer: SU201
- Valbar kurs: Nej
- Förkunskapskrav

**Användning**:
- Testa metadata-extraktion
- Validera mer komplex dokumentstruktur
- Testa tekniska termer och begrepp

---

### 4. skolverket-matematik.txt
**Typ**: Skolverket Gymnasiekursplan  
**Ämne**: Matematik 1c  
**Poäng**: 100 poäng (Gymnasiepoäng, inte yhp)  
**Innehåll**:
- ## Ämnets syfte
- ### Centralt innehåll
- ### Kunskapskrav (Betyg E, C, A)

**Användning**:
- Testa Skolverkets format (annorlunda än YH)
- Validera betygskriterier (E/C/A istället för IG/G/VG)
- Testa ## och ### markdown-struktur

---

### 5. simple-text.txt
**Typ**: Enkel textfil  
**Innehåll**: Grundläggande text med svenska tecken  

**Användning**:
- Testa UTF-8 encoding
- Validera grundläggande filuppladdning
- Snabba smoke tests

---

### 6. expected-output-example.md
**Typ**: Dokumentation  
**Innehåll**: Förväntat resultat från kursplansanalys  

**Användning**:
- Referens för förväntad output
- Validera att FormatResultWithCodes fungerar korrekt
- Dokumentation för utvecklare

---

## Dokumenttyper som stöds

### YH-Kursplan (Yrkeshögskola)
**Kännetecken**:
- yhp (yrkeshögskolepoäng)
- Strukturen: Kunskaper, Färdigheter, Kompetenser
- Betygsskala: IG (Icke Godkänt), G (Godkänt), VG (Väl Godkänt)
- Format: #Sektion följt av punktlista

**Detekteringsnyckleord**:
- "yhp", "yrkeshögskola"
- "efter genomgången kurs ska den studerande"
- "kunskapskrav/lärandemål"

---

### Skolverket Kursplan (Gymnasie/Grundskola)
**Kännetecken**:
- Gymnasiepoäng (inte yhp)
- Strukturen: Ämnets syfte, Centralt innehåll, Kunskapskrav
- Betygsskala: E, C, A (eventuellt D, B)
- Format: ## och ### markdown

**Detekteringsnyckleord**:
- "skolverket", "gymnasieskola"
- "betyg e", "betyg c", "betyg a"
- "ämnets syfte", "centralt innehåll"

---

## Extraktionsregler

### Numrering
1. **Löpande numrering** över alla sektioner
2. **Format**: Sektionskod + tvåsiffrigt nummer (01, 02, ..., 10, 11, ...)
3. **Koder**:
   - K = Kunskaper
   - F = Färdigheter
   - Ko = Kompetenser
   - CI = Centralt innehåll

### Exempel:
```
Sektion 1: K01, K02, K03
Sektion 2: F04, F05, F06  (fortsätter från 04, inte från 01)
Sektion 3: Ko07, Ko08     (fortsätter från 07)
```

---

## Outputformat

### FormatResultWithCodes (nuvarande)
```markdown
## [Kursnamn]
 *([Poäng])*

# [Sektion 1]
**[Kod]** - [Text]
**[Kod]** - [Text]

# [Sektion 2]
**[Kod]** - [Text]
```

### VIKTIG NOT
- **INGEN sammanfattningstabellogg** ska inkluderas i output
- FormatResultWithCodes ska BARA visa kursnamn och sektioner med mål
- Svenska tecken (åäö ÅÄÖ) ska bevaras

---

## Testscenarier

### 1. Basic Extraction
```csharp
var result = KursplanService.ExtractKursmal(fileContent, DocumentType.SwedishYhKursplan);
Assert.True(result.TotalItems > 0);
```

### 2. Swedish Characters
```csharp
var text = "Kunskapsmål med åäö ÅÄÖ";
var result = KursplanService.ExtractKursmal(text, DocumentType.SwedishKursplan);
Assert.Contains("åäö", result.Sections[0].Items[0].Text);
```

### 3. Correct Numbering
```csharp
var result = KursplanService.ExtractKursmal(fileContent, DocumentType.SwedishYhKursplan);
var formatted = KursplanService.FormatResultWithCodes(result);

// Should continue numbering: K01-K10, then F11-F16, then Ko17-Ko21
Assert.Contains("K01", formatted);
Assert.Contains("F11", formatted); // NOT F01
Assert.Contains("Ko17", formatted); // NOT Ko01
```

### 4. No Summary Table
```csharp
var formatted = KursplanService.FormatResultWithCodes(result);
Assert.DoesNotContain("Sammanfattning", formatted);
Assert.DoesNotContain("| Sektion | Kod | Antal |", formatted);
```

---

## Användning i tester

### Ladda testdata:
```csharp
var testDataPath = Path.Combine("TestData", "sample-kursplan.txt");
var content = File.ReadAllText(testDataPath, Encoding.UTF8);
```

### Kör extraktion:
```csharp
var result = KursplanService.ExtractKursmal(content, DocumentType.SwedishYhKursplan);
```

### Validera resultat:
```csharp
Assert.Equal(21, result.TotalItems); // 10 + 6 + 5
Assert.Equal(3, result.Sections.Count);
Assert.Equal("Kunskaper", result.Sections[0].SectionName);
```

---

## Vanliga problem och lösningar

### Problem: Svenska tecken blir "Ã¤Ã¶Ã¥"
**Lösning**: Använd alltid `Encoding.UTF8`:
```csharp
File.ReadAllText(path, Encoding.UTF8);
Encoding.UTF8.GetBytes(text);
```

### Problem: Numreringen börjar om för varje sektion
**Lösning**: KursplanService ska räkna löpande över alla sektioner

### Problem: Sammanfattningstabellen visas fortfarande
**Lösning**: FormatResultWithCodes ska INTE innehålla sammanfattning

---

## Bidrag

När du lägger till nya testfiler:
1. Lägg till UTF-8 BOM om möjligt
2. Inkludera svenska tecken (åäö) för att testa encoding
3. Dokumentera förväntad output här
4. Lägg till motsvarande test i DocumentAnalysisTests.cs
