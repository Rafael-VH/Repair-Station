# 🤖 Estação de Reparo de Robôs

## Um Mod de RimWorld BioTech

![RimWorld](https://img.shields.io/badge/RimWorld-1.6-8B4513?style=flat-square)
![BioTech DLC](https://img.shields.io/badge/BioTech_DLC-Obrigatório-6A0DAD?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-4.8-239120?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-10.0-239120?style=flat-square&logo=csharp)
![Licença](https://img.shields.io/badge/Licença-MIT-blue?style=flat-square)

**Estação de acoplamento de reparo automático para mecanoídes do BioTech.**
Quando danificados, seus mecanoídes buscarão a estação de forma autônoma e se repararão sozinhos — sem necessidade de microgerenciamento.

> 📖 **English documentation** available at [`../../README.md`](../../README.md)
> 📖 **Documentación en español** disponible en [`../es/README.md`](../es/README.md)

---

## ✨ Funcionalidades

- **Reparo autônomo** — Os mecanoídes detectam quando sua saúde cai abaixo de um limite configurável e navegam até a estação disponível mais próxima sem intervenção do jogador
- **Consumo de recursos** — O reparo consome aço dos estoques próximos, gerenciado por um buffer interno (até 50 unidades) para reduzir as buscas no mapa por ciclo
- **Requer energia** — Necessita de uma conexão elétrica ativa; a estação desliga-se corretamente quando a energia é perdida
- **Ejeção manual** — Um botão de gizmo permite ao jogador remover à força um mecanoíde no meio do reparo
- **Totalmente configurável** — Todos os parâmetros (limite de saúde, velocidade de reparo, custo de aço, alcance de detecção) são editáveis no XML sem necessidade de recompilar
- **Bloqueado por pesquisa** — Desbloqueado por *Sistemas de Reparo de Mecanoídes* (nível Espacial, 1200 pts), requer *Noções Básicas de Mecanoídes* primeiro
- **Pode sofrer avarias** — Requer manutenção periódica, consistente com os edifícios industriais do jogo base
- **Seguro para salvar/carregar** — Todo o estado (ocupante, buffer de aço) é serializado com `Scribe_References` e `Scribe_Values`
- **Sem perda de partes do corpo** — Repara apenas lesões ativas; danos permanentes não são restaurados (por design)
- **Sem dependência do Harmony** — Toda a integração de IA é feita via patches XML no ThinkTree, mantendo o risco de compatibilidade baixo

---

## 🏗️ Visão Geral da Arquitetura

O mod é construído em torno de quatro sistemas interconectados:

```text
┌─────────────────────────────────────────────────────────────────┐
│                    CAMADA DE IA (ThinkTree)                     │
│                                                                 │
│  ThinkNode_ConditionalNeedsRepair                               │
│    └─ Verifica: é mecanoíde? do jogador? saúde < limite?        │
│       └─ JobGiver_GoToRepairStation                             │
│            └─ Emite: job RRS_GoToRepairStation                  │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                      CAMADA DE JOBS                             │
│                                                                 │
│  JobDriver_GoToRepairStation                                    │
│    1. GotoThing → andar até InteractionCell                     │
│    2. dock (Instant) → TryAcceptOccupant → enfileirar reparo    │
│                                                                 │
│  JobDriver_RepairAtStation                                      │
│    - Aguarda (ToilCompleteMode.Never)                           │
│    - Termina quando CurrentOccupant passa a null                │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                    CAMADA DO EDIFÍCIO                           │
│                                                                 │
│  Building_RobotRepairStation                                    │
│    - Gerencia ocupante (TryAcceptOccupant / EjectOccupant)      │
│    - Tick: TryConsumeSteel a cada repairTickInterval            │
│    - Buffer de aço (até 50 uds) evita buscas por tick           │
│    - Gizmos, InspectString, salvar/carregar                     │
│                                                                 │
│  CompRobotRepairStation (ThingComp)                             │
│    - CompTick: ApplyRepairTick a cada repairTickInterval        │
│    - Cura todas as instâncias Hediff_Injury ativas              │
│    - Chama OnRepairComplete quando saúde ≥ 99%                  │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                    CAMADA DE REGISTRO                           │
│                                                                 │
│  RepairStationTracker (MapComponent)                            │
│    - Registro/remoção em O(1) em SpawnSetup / DeSpawn           │
│    - ThinkNodes iteram esta lista em vez de buscar no mapa      │
│    - Declarado em MapComponentDefs.xml; instanciado pelo RW     │
└─────────────────────────────────────────────────────────────────┘
```

---

## 📁 Estrutura de Pastas

```text
RobotRepairStation/
│
├── About/
│   └── About.xml                        ← Metadados do mod, packageId, dependência BioTech
│
├── Assemblies/
│   └── RobotRepairStation.dll           ← Saída compilada (não editar manualmente)
│
├── 1.6/
│   ├── Defs/
│   │   ├── JobDefs/
│   │   │   └── JobDefs_RobotRepair.xml      ← RRS_GoToRepairStation + RRS_RepairAtStation
│   │   ├── MapComponentDefs/
│   │   │   └── MapComponentDefs.xml         ← Registra RepairStationTracker no RimWorld
│   │   ├── ResearchProjectDefs/
│   │   │   └── ResearchDefs.xml             ← "Sistemas de Reparo de Mecanoídes" (Espacial, 1200 pts)
│   │   └── ThingDefs/
│   │       └── Buildings_RobotRepairStation.xml  ← ThingDef: tamanho, custo, comps, pesquisa
│   │
│   ├── Languages/
│   │   ├── English/Keyed/
│   │   │   └── RobotRepairStation.xml       ← Strings visíveis ao jogador (locale base)
│   │   ├── Spanish/Keyed/
│   │   │   └── RobotRepairStation.xml       ← Tradução para espanhol (Espanha)
│   │   ├── SpanishLatin/Keyed/
│   │   │   └── RobotRepairStation.xml       ← Tradução para espanhol (América Latina)
│   │   └── Portuguese/Keyed/
│   │       └── RobotRepairStation.xml       ← Tradução para português
│   │
│   └── Patches/
│       └── MechanoidThinkTree.xml           ← Injeta nó de reparo em MechanoidConstant
│
├── Source/
│   ├── RRS_Mod.cs                           ← Bootstrap StaticConstructorOnStartup
│   ├── RRS_JobDefOf.cs                      ← Referências estáticas de jobs [DefOf]
│   ├── Building_RobotRepairStation.cs       ← Edifício principal: ocupante, aço, UI
│   ├── CompProperties_RobotRepairStation.cs ← CompProperties + CompRobotRepairStation (tick de cura)
│   ├── JobDriver_GoToRepairStation.cs       ← Driver do job andar-até-estação
│   ├── JobDriver_RepairAtStation.cs         ← Driver do job reparo acoplado
│   ├── ThinkNode_ConditionalNeedsRepair.cs  ← Condicional IA + JobGiver + RepairStationUtility
│   └── RepairStationTracker.cs              ← Registro de estações como MapComponent
│
├── Textures/
│   └── Things/
│       └── Buildings/
│           └── RobotRepairStation.png       ← Sprite do edifício 128×128 (deve ser adicionado)
│
├── docs/
│   ├── es/
│   │   └── README.md                        ← Documentação em espanhol
│   └── pt/
│       └── README.md                        ← Este arquivo — documentação em português
│
└── .vscode/
    ├── mod.csproj                           ← Arquivo de projeto (net480, x64)
    ├── tasks.json                           ← Tarefas de compilação (Windows + Linux)
    ├── launch.json                          ← Configurações de execução e depurador
    └── extensions.json                      ← Extensões recomendadas para VS Code
```

---

## ⚙️ Referência de Configuração

Todos os parâmetros podem ser ajustados diretamente em `1.6/Defs/ThingDefs/Buildings_RobotRepairStation.xml` dentro do bloco `<li Class="RobotRepairStation.CompProperties_RobotRepairStation">` — sem necessidade de recompilar.

| Propriedade | Padrão | Descrição |
| --- | --- | --- |
| `repairHealthThreshold` | `0.5` | Fração de saúde (0–1) abaixo da qual o mecanoíde busca reparo. `0.5` = 50%. |
| `repairSpeedPerTick` | `0.0005` | HP restaurados por tick de jogo em cada lesão ativa. |
| `steelPerRepairCycle` | `1` | Unidades de aço consumidas por intervalo de reparo. Com os valores padrão, ~7,2 unidades/hora. |
| `repairTickInterval` | `500` | Ticks entre cada ciclo de consumo de aço e cura (~8,3 s na velocidade ×1). Controla tanto a granularidade dos recursos quanto o custo de CPU. |
| `maxRepairRange` | `30` | Distância máxima em células para que um mecanoíde detecte esta estação e se desloque até ela. |

> **Dica de ajuste:** `repairSpeedPerTick` e `repairTickInterval` estão acoplados. O HP efetivo curado por segundo é simplesmente `repairSpeedPerTick × 60`.

---

## 🔬 Como Funciona o Reparo (Passo a Passo)

1. A cada tick de IA, `ThinkNode_ConditionalNeedsRepair.Satisfied()` avalia cada mecanoíde do jogador nesta ordem (verificações mais baratas primeiro):
   - É um mecanoíde? É do jogador?
   - Já está executando um job de reparo (`RRS_RepairAtStation` ou `RRS_GoToRepairStation`)?
   - Existe uma estação alimentada, livre e alcançável dentro de `maxRepairRange`? *(a mais custosa — executada por último)*
   - A saúde está abaixo de `repairHealthThreshold`?

2. Se todas as condições forem atendidas, `JobGiver_GoToRepairStation` emite um job `RRS_GoToRepairStation` apontando para a estação válida mais próxima, verificando antes que nenhum outro pawn da mesma facção já a tenha reservado.

3. `JobDriver_GoToRepairStation` leva o mecanoíde até a `InteractionCell` da estação, então chama `Building_RobotRepairStation.TryAcceptOccupant()` e enfileira `RRS_RepairAtStation`. Se uma condição de corrida preencher a estação entre a caminhada e o acoplamento, o job termina como `Incompletable` e o mecanoíde tentará novamente.

4. A cada `repairTickInterval` ticks enquanto acoplado:
   - **Tick do edifício:** `TryConsumeSteel()` desconta do buffer interno. Se o buffer estiver vazio, busca aço em um raio de 8 células e recarrega até 50 unidades. Se não encontrar, o mecanoíde é ejetado e o jogador é notificado.
   - **Tick do comp:** `ApplyRepairTick()` chama `injury.Heal(repairSpeedPerTick)` em cada `Hediff_Injury` ativa (não permanente). A cura é ignorada se o buffer de aço estiver vazio após o tick do edifício.

5. Quando `SummaryHealthPercent ≥ 0.99`, `OnRepairComplete()` é acionado: o jogador recebe uma mensagem positiva, `CurrentOccupant` é definido como `null`, e o `tickAction` de `JobDriver_RepairAtStation` detecta a mudança e encerra o job corretamente.

6. Ao carregar, `PostMapInit()` valida que o ocupante serializado ainda possui um job de reparo ativo ou na fila. Se não (ex.: save editado externamente), a referência ao ocupante é limpa e um aviso é registrado, evitando que a estação fique bloqueada permanentemente.

---

## 🧱 Estatísticas do Edifício

| Estatística | Valor |
| --- | --- |
| Tamanho | 2×2 células |
| HP máximo | 300 |
| Trabalho para construir | 4.000 ticks |
| Inflamabilidade | 50% |
| Beleza | −2 |
| Consumo elétrico | 250W |
| Custo | 150 Aço + 4 Componentes Industriais + 1 Componente Espacial |
| Pesquisa | Sistemas de Reparo de Mecanoídes (Espacial, 1200 pts) |
| Pesquisa prévia necessária | Noções Básicas de Mecanoídes (DLC BioTech) |
| Raio de busca de aço | 8 células |
| Capacidade do buffer de aço | 50 unidades |

---

## 🔧 Compilação

### Pré-requisitos

- .NET SDK para `net480` (Visual Studio 2022 / JetBrains Rider ou CLI do `dotnet`)
- RimWorld 1.6 instalado via Steam
- Pacote NuGet `Krafs.Rimworld.Ref` (resolvido automaticamente ao compilar)

### Visual Studio / Rider

1. Abra `.vscode/mod.csproj`.
2. Compilar → Release. O DLL é gerado automaticamente em `1.6/Assemblies/` como `RepairStation.dll`.

### Linha de Comando

```bash
cd .vscode
dotnet build -c Release
```

### VS Code

Use a tarefa **Build & Run** (`Ctrl+Shift+B`) definida em `.vscode/tasks.json`. Uma configuração de execução separada **Attach Debugger** conecta o Mono na porta `56000` para depuração ao vivo.

### Caminhos Padrão do RimWorld

| SO | Caminho |
| --- | --- |
| Windows | `C:\Program Files (x86)\Steam\steamapps\common\RimWorld` |
| Linux | `~/.steam/steam/steamapps/common/RimWorld` |
| macOS | `~/Library/Application Support/Steam/steamapps/common/RimWorld` |

---

## 🖼️ Adicionando a Textura

Coloque um PNG de **128 × 128 px** em:

```text
Textures/Things/Buildings/RobotRepairStation.png
```

**Guia de estilo:** Siga a estética do BioTech — painéis de metal escuro com iluminação de destaque em azul/verde-azulado. O edifício ocupa 2×2 células; mantenha o sprite visualmente centralizado com um motivo sutil de braço ou berço de acoplamento. O ThingDef usa `Graphic_Single`, portanto a textura não rotaciona — projete para uma perspectiva de cima para baixo, voltada para o sul.

---

## 📦 Instalação

1. Copie a pasta `RobotRepairStation/` para o diretório de mods do RimWorld:

    | SO | Diretório de Mods |
    | --- | --- |
    | Windows | `%APPDATA%\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Mods\` |
    | Linux | `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Mods/` |
    | macOS | `~/Library/Application Support/RimWorld/Mods/` |

2. Ative o mod no jogo. Certifique-se de que o **DLC BioTech** está ativo.

3. Pesquise *Noções Básicas de Mecanoídes* e depois *Sistemas de Reparo de Mecanoídes* para desbloquear o edifício.

---

## 🌍 Localização

Todas as strings visíveis ao jogador estão em `1.6/Languages/<Idioma>/Keyed/RobotRepairStation.xml`. Idiomas incluídos atualmente:

| Idioma | Caminho |
| --- | --- |
| Inglês | `1.6/Languages/English/Keyed/` |
| Espanhol (Espanha) | `1.6/Languages/Spanish/Keyed/` |
| Espanhol (América Latina) | `1.6/Languages/SpanishLatin/Keyed/` |
| Português | `1.6/Languages/Portuguese/Keyed/` |

Para adicionar uma nova tradução:

1. Crie `1.6/Languages/<NomeDoIdioma>/Keyed/RobotRepairStation.xml`
2. Copie o arquivo em inglês e substitua os valores (mantenha as chaves idênticas)
3. O RimWorld usa o inglês automaticamente como fallback para qualquer chave ausente

Todas as chaves usam o prefixo `RRS_` para evitar colisões com outros mods. Chaves contendo `{0}` são strings de formato — preenchidas em tempo de execução com valores como o rótulo curto do mecanoíde.

---

## ⚠️ Limitações Conhecidas e Decisões de Design

- **Sem regeneração de partes do corpo perdidas** — Lesões do tipo `HediffComp_GetsPermanent` são intencionalmente ignoradas em `ApplyRepairTick()`. Regenerar membros está fora do escopo deste nível de edifício.
- **Um único ocupante por estação** — A estação suporta exatamente um mecanoíde por vez. Coloque múltiplas estações para esquadrões maiores de mecanoídes.
- **O raio de busca de aço é de 8 células** — O aço deve estar estocado perto da estação. O buffer interno (máx. 50 unidades) reduz a frequência de busca.
- **O patch do ThinkTree aponta para `MechanoidConstant`** — Se a Ludeon reestruturar esta árvore em uma atualização futura, o XPath em `1.6/Patches/MechanoidThinkTree.xml` pode precisar de atualização. Sintoma: os mecanoídes nunca buscam a estação de forma autônoma.
- **Sem patches do Harmony** — Toda a integração de IA é feita via patches XML no ThinkTree, mantendo o risco de compatibilidade baixo.
- **O nome do assembly é `RepairStation`** — O `RootNamespace` e `AssemblyName` no `.csproj` estão definidos como `RepairStation`, enquanto o namespace C# em todo o código-fonte é `RobotRepairStation`. Lembre-se desta distinção ao renomear qualquer um dos dois.

---

## 📜 Licença

MIT — livre para usar, modificar e distribuir com atribuição.

---

Construído para RimWorld 1.6 · Requer o DLC BioTech · Autor: RexThar
