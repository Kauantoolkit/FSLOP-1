# Toolchain

O que este projeto usa, na versão exata, e de onde vem. `tools/godot/` está no
`.gitignore` porque é binário de terceiro — o que é fonte é **este arquivo**.

Apurado na máquina em **06/09/2026**, com o comando ao lado. Nada aqui foi lembrado.

| ferramenta | versão | como foi apurada |
|---|---|---|
| Windows | 11 Home Single Language 10.0.26200 | ambiente da sessão |
| Unity | `6000.6.0f1` | `ls "C:/Program Files/Unity/Hub/Editor"` |
| Godot | `4.7.2-stable` | API de releases do GitHub, `godotengine/godot` |
| Python | `3.12.3` | `python --version` |
| Node | `v25.1.0` | `node --version` |
| .NET SDK | `10.0.400` | `dotnet --version` |
| git | `2.49.0.windows.1` | `git --version` |
| Steam | instalado em `D:\jogos\Steam` | `HKCU:\Software\Valve\Steam` |

## Hardware de referência

Este é o hardware que vai no relatório da Fase 1. Ele **é** parte do resultado: um
número de fps sem a máquina ao lado não significa nada.

| | |
|---|---|
| CPU | AMD Ryzen 5 5600 — 6 núcleos / 12 threads |
| RAM | 15,9 GB |
| GPU | NVIDIA GeForce RTX 3060 — driver 32.0.15.9186 |
| VRAM | **NÃO APURADA** |
| Disco | C: 122 GB livres · D: 484 GB · E: 543 GB |

Duas ressalvas que precisam sobreviver até o relatório:

- **VRAM não apurada.** O `AdapterRAM` do WMI devolveu 4 GB, mas esse campo é de 32
  bits e satura — não é o valor real e não pode ser publicado. Apurar por
  `nvidia-smi` antes de escrever o relatório.
- **A máquina tem um Parsec Virtual Display Adapter.** Medir fps por sessão remota
  passa por outro caminho de apresentação e dá número não-reprodutível. Toda corrida
  precisa declarar: monitor local ou Parsec. O `[SOAK-META]` ainda **não** carrega esse
  campo — acrescentar antes da primeira medição de fps que valha como resultado.

## Godot — instalação portátil

Decidido em `tasks/FSLOP-1/decisions/03`: nada de instalador, nada de PATH global,
nada de registro. O zip oficial é extraído em `tools/godot/4.7.2/` e desfazer é apagar
a pasta.

**Ainda não baixado.** Só entra na Fase 1-C, que é a última da ordem (`decisions/04`).

A camada de rede da candidata C — GodotSteam como módulo, como GDExtension, ou
`steam-multiplayer-peer` — **não está escolhida**. A compatibilidade delas com 4.7.x
muda por release, então será decidida lendo a documentação da versão exata no momento
da Fase 1-C, nunca de memória.
