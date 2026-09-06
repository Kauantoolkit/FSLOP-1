# Candidata B — versões exatas

Toda medição desta stack vale contra **isto**. Apurado em 06/09/2026 lendo
`Packages/packages-lock.json` e os `package.json` instalados em
`Library/PackageCache` — não a partir do que o `manifest.json` pediu.

| peça | pedido no manifesto | **instalado de fato** | fonte |
|---|---|---|---|
| Unity | — | `6000.6.0f1` (rev `f7f8ed4d1e24`) | `ProjectSettings/ProjectVersion.txt` |
| FishNet | tag `4.7.3` | git `73f30cf2425dc808a4f463f0a233d386010810b3` | `packages-lock.json` |
| Steamworks.NET | `2025.164.1` | `2025.164.1`, registro OpenUPM | `packages-lock.json` |
| FishySteamworks | **não está no manifesto** | tag `4.1.1`, copiado para `Assets/` | ver abaixo |
| Newtonsoft JSON | (dependência do FishNet) | `3.2.2`, registro Unity | `packages-lock.json` |
| ugui | (dependência do FishNet) | `2.6.0`, builtin | `packages-lock.json` |

## A versão que o Unity mostra não é a instalada

O `package.json` que veio dentro do pacote FishNet da tag `4.7.3` declara
`"version": "4.7.2"`. O Package Manager vai exibir **4.7.2**. O código é o da tag
`4.7.3`, e o que vale é o hash git da tabela acima. Não relatar a versão da tela.

## FishySteamworks não é dependência versionada, e isso é um problema

Ele **não pode** ser instalado por UPM:

- pelo git na raiz, chega **vazio** — o `.gitignore` do repo tem `*/` (ignora todo
  diretório) e as exceções apontam para um caminho que não existe mais na tag;
- por `?path=FishNet/Plugins/FishySteamworks`, chega completo mas **não compila** —
  o pacote não tem `.asmdef`, e o Unity não compila script de pacote UPM sem um.

Então o código foi **copiado** de
`FishySteamworks-4.1.1/FishNet/Plugins/FishySteamworks/` para
`Assets/FishNet/Plugins/FishySteamworks/` — 21 arquivos, incluindo os `.meta`
originais (os GUIDs vêm do autor, não foram regerados).

Origem do arquivo: `https://github.com/FirstGearGames/FishySteamworks/archive/refs/tags/4.1.1.tar.gz`,
baixado em 06/09/2026. O commit da tag é `2024-08-26T18:50:09Z`.

**O que isso custa:** não existe pino de versão para o transporte no `manifest.json`.
Quem clonar o repositório recebe a cópia que está em `Assets/`, o que na prática
funciona — mas atualizar ou auditar a versão dele é trabalho manual, e uma máquina
nova não tem como conferir que recebeu a mesma coisa a não ser comparando arquivos.

## Compila?

Sim, e é o primeiro resultado real da candidata B.

```
Assembly-CSharp.dll   25.600 bytes
error CS               0
tipos presentes        FishySteamworks, ClientHostSocket, ServerSocket,
                       BidirectionalDictionary, e referência a FishNet.Transporting
```

Um transporte de agosto de 2024 compila limpo contra FishNet 4.7.3 e Unity 6.
**Compilar não é funcionar** — se a deriva de 2 anos quebra alguma coisa, quebra em
runtime, e quem responde é a change 06 (lobby Steam).
