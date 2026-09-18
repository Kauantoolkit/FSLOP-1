# Candidata B — Unity 6 + FishNet + FishySteamworks

Resultados medidos. **Nenhum número aqui foi estimado**; cada um veio de uma corrida com
saída registrada em `tasks/FSLOP-1/changes/`.

## Âncora

| | |
|---|---|
| Repositório | `projects/FSLOP-1`, branch `main` |
| Build medida | `b5a368b` (a seção de rede) · `4333f7b` (a seção de instância única) |
| Data | 18/09/2026 (rede) · 17/09/2026 (instância única) |
| Engine | Unity `6000.6.0f1` |
| FishNet | `4.7.3` · Steamworks.NET `2025.164.1` · FishySteamworks `4.1.1` |
| Máquina | Ryzen 5 5600 (6C/12T) · 15,9 GB · RTX 3060 (12113 MB, lido do log do player) · Windows 11 |
| Display | `local` (monitor físico, **não** sessão Parsec) |

## O veredito, em uma linha

**A candidata B replica o mundo inteiro e entrega late join e queda de host limpos. Em rede
boa, o único problema é atraso. A partir de ~3% de perda, a viga teleporta no cliente — e isso
é uma exigência literal do briefing que ela não cumpre.**

Em rede local sem degradação, os **151 corpos** chegam ao cliente em 135 ms e o estado dele
está correto: descontado o atraso, a divergência sobre o mundo inteiro é `0,020 u` contra o
limiar de `0,15`. O que reprova o drift é o cliente ficar **~4,3 ticks atrás** (≈143 ms),
pondo o erro visível em `0,67 u`.

Sob os **150 ms que o briefing manda simular**, o atraso dobra — previsto antes de medir — e o
erro visível vai a `1,28 u`. Até **1% de perda** a divergência continua desprezível
(`0,019 u`). A partir de **3%** ela salta para `0,35`–`0,39 u` **e a viga dá saltos de
`0,689 u` e `0,743 u` no cliente**, acima do limiar de `0,5`. O efeito é **bimodal**: uma
corrida a 3% não teve salto nenhum. Ver `docs/99` itens 19 e 21.

**O que passa nas condições exatas do briefing:** o late join entrega os 151 corpos em
`236,5 ms`, a queda do host encerra limpa e não há exceção em 10 min.

## Soak de 10 min com duas instâncias — saída real do avaliador

Host 660 s; cliente entrando aos **180 s** (o late join de 3 min do briefing) e saindo junto
com o host. Tugboat local, **zero RTT injetado**. Build `b5a368b`, `display=local`.

```
PASS  integridade         2 arquivos, 1138 amostras, 13 eventos, 204891 posicoes, 24055 linha(s) fora do contrato
INFO  procedencia         transporte=local rtt_injetado=0ms perda_injetada=0.0%  <<< transporte LOCAL: estes numeros NAO valem como 'sobre o relay da Steam'
PASS  duracao             cobertura 659.2 s (minimo 600 s) = span 658.2 + 1 intervalo de 0.98 s
PASS  relogio_coerente    client/1: 50.0 tick/s mediano; host/0: 50.0 tick/s mediano
PASS  fps_host            100.00% das 659 amostras com fps>=60 e p99<=16.67ms (exigido 99%)
INFO  banda_por_cliente   id=1 BANDA NAO INSTRUMENTADA (rx/tx vieram -1 ou ausentes)
PASS  viga_nao_teleporta  maior salto 0.076 u (limite 0.50) em t=180.977 id=0 | 0 amostra(s) acima
FAIL  resposta_do_input   nenhuma amostra de cliente com input_ms_p99 medido (ausente, ou -1 = nao instrumentado)
FAIL  drift               pior id=1: drift_mesmo_ntick max 0.6736 u (obj=464 ntick=9016) p99 0.6133 u (limite 0.15) | alinhado 0.0200 u com atraso de 4.3 tick(s) | 151 corpo(s) e 64800 par(es) comparados, 1 cliente(s) acima do limite
PASS  late_join           1 late join(s), 151 de 151 corpo(s) em cada; pior elapsed 134.8 ms
PASS  queda_do_host       1 de 1 cliente(s) encerraram com clean=1
PASS  zero_excecoes       nenhuma linha ev=exception
```

Eventos reais dos dois logs, **inteiros**:

```
[SOAK-EV] t=0.158   tick=1     role=host   id=0 ev=spawn_done bodies=151
[SOAK-EV] t=180.198 tick=9003  role=host   id=0 ev=peer_connected conn=0
[SOAK-EV] t=180.243 tick=9005  role=host   id=0 ev=peer_authenticated conn=0 cena=SpikeB
[SOAK-EV] t=660.001 tick=32994 role=host   id=0 ev=host_quit motivo=duracao_atingida
[SOAK-EV] t=660.001 tick=32994 role=host   id=0 ev=shutdown clean=1 reason=duracao_atingida excecoes=0

[SOAK-EV] t=0.114   tick=0     role=client id=1 ev=late_join_begin at_t=0.114 corpos_congelados=1
[SOAK-EV] t=0.250   tick=5     role=client id=1 ev=late_join_done elapsed_ms=134.8 ntick=5407 world_hash=6ee2e824 bodies=151 esperados=151
[SOAK-EV] t=479.983 tick=23993 role=client id=1 ev=peer_disconnected conn=eu
[SOAK-EV] t=479.983 tick=23993 role=client id=1 ev=shutdown clean=1 reason=host_lost excecoes=0
```

### Os dois FAIL, um a um

| FAIL | é defeito da stack? | o que é |
|---|---|---|
| `drift` | **não** — é latência | estado certo nos 151 corpos (`alinhado 0,020`), atraso de 4,3 ticks. `p99 ≈ max` (0,61 contra 0,67) ⇒ é regime, não pico |
| `resposta_do_input` | **não** — não instrumentado | exige personagem **predito** no cliente, que ainda não existe |

O pior caso do drift é `obj=464` em `ntick=9016` — **13 ticks depois de o cliente conectar**
(`peer_connected` em `ntick≈9003`). Ou seja, o pico está no instante da entrada. Mas o `p99` de
`0,6133` mostra que o regime não está longe dele: não é um transiente de entrada seguido de
bom comportamento.

## A mesma corrida sob 150 ms de RTT e 3% de perda

Condição explícita do briefing, cumprida pela primeira vez em 18/09. Mesmo roteiro — host
660 s, cliente entrando aos 180 s — no **development build** (`Build/SpikeB-dev`), que é a
única versão em que o simulador do FishNet tem efeito (`docs/99` 20). Build `dc413bc`.

```
INFO  procedencia         transporte=local rtt_injetado=150ms perda_injetada=3.0% build=dc413bc flavor=development  <<< DEVELOPMENT build: sem stripping e com hooks de profiler. fps daqui NAO vale como fps de release
PASS  duracao             cobertura 659.2 s (minimo 600 s)
PASS  relogio_coerente    client/1: 50.0 tick/s mediano; host/0: 50.0 tick/s mediano
PASS  viga_nao_teleporta  maior salto 0.072 u (limite 0.50) em t=180.991 id=0 | 0 amostra(s) acima
FAIL  resposta_do_input   nenhuma amostra de cliente com input_ms_p99 medido
FAIL  drift               pior id=1: drift_mesmo_ntick max 1.2761 u (obj=0 ntick=19215) p99 1.1182 u (limite 0.15) | alinhado 0.0903 u com atraso de 8.4 tick(s) | 151 corpo(s) e 64800 par(es) comparados
PASS  late_join           1 late join(s), 151 de 151 corpo(s) em cada; pior elapsed 236.5 ms
PASS  queda_do_host       1 de 1 cliente(s) encerraram com clean=1
PASS  zero_excecoes       nenhuma linha ev=exception
```

**O `fps` desta corrida não está citado de propósito.** É development build: sem stripping,
com hooks de profiler. O avaliador imprime o aviso, e o número não entra em lugar nenhum.

### O que isto responde do briefing, e o que não

O briefing pede, literalmente: *"Sob 150ms RTT e 3% packet loss simulados: objeto carregado
**não teleporta** e o personagem **responde em menos de 100ms percebidos**"*. São duas
exigências:

| exigência | estado |
|---|---|
| objeto carregado não teleporta | **FAIL** — `0,689 u` e `0,743 u` observados **no cliente**, contra o limiar de `0,5`. Ver a varredura abaixo |
| personagem responde < 100 ms | **não medido** — exige personagem predito no cliente |

> **Correção de 18/09.** Uma versão anterior deste arquivo dizia `PASS` nessa primeira linha,
> com `0,072 u`. Dois erros: aquele número é do **host** (`id=0`), onde não há rede; e a única
> corrida examinada era uma em que o salto não aconteceu. Para um requisito da forma *"não
> teleporta"*, **uma ocorrência derruba e uma corrida limpa não prova nada** — eu estava lendo
> a ausência de um evento raro como prova da impossibilidade dele.

### A varredura de perda, com RTT fixo em 150 ms

Corridas de 400 s com o cliente entrando aos 100 s, mais a de 10 min a 3% para comparação.
Todos os números com **alinhamento sub-tick** (`changes/22`).

| perda | `drift_alinhado` | atraso | maior salto da viga **no cliente** |
|---|---|---|---|
| 0% | `0,0180 u` | 7,9 t | dentro do limite |
| 1% | `0,0191 u` | 7,5 t | dentro do limite |
| **3%** (400 s) | **`0,3541 u`** | 7,6 t | **`0,689 u` — REPROVA** |
| **5%** (400 s) | **`0,3857 u`** | 8,1 t | **`0,743 u` — REPROVA** |
| 3% (10 min) | `0,0903 u` | 8,4 t | dentro do limite |

**O efeito é bimodal, não uma rampa.** A 3% houve uma corrida com `0,354` e teleporte e outra
com `0,090` e nenhum. A perda é sorteada, e o que provavelmente produz o salto é uma **rajada**
de pacotes perdidos, não a taxa média — ou seja, **a taxa sozinha não prevê o comportamento**.

**Até 1% de perda, a divergência é desprezível** (`0,018`–`0,019 u`, contra o limiar de `0,15`).
A fronteira está entre 1% e 3%, com **um ponto de cada lado** e um contraexemplo dentro do 3%.
Detalhe e o que falta saber em `docs/99` item 21.

**O atraso dobrar era previsto antes de medir** — 150 ms ÷ 33 ms por tick ≈ 4,5 somados aos
~4,3 sem injeção; previsto "8 a 9", medido 7,5 a 8,4.

### Reprodução

Três corridas independentes de 10 min, em builds diferentes:

| | corrida 1 | corrida 2 | corrida 3 |
|---|---|---|---|
| build | `95b7678` | `55745ae` | `b5a368b` |
| corpos comparados | 1 | 1 | **151** |
| `drift_mesmo_ntick` max | `0.7185 u` | `0.6824 u` | `0.6736 u` |
| `drift_mesmo_ntick` p99 | `0.6820 u` | `0.6498 u` | `0.6133 u` |
| `drift_alinhado` | `0,0181 u` | `0,0205 u` | `0,0200 u` |
| atraso | `4,6 t` | `4,4 t` | `4,3 t` |
| pares comparados | 10 653 | 10 801 | **64 800** |

Mesmo regime nas três: resíduo de `0,018`–`0,020 u` e atraso de `4,3`–`4,6` ticks (143–153 ms
a 30 Hz).

> **Os três `drift_alinhado` acima foram recalculados em 18/09** com alinhamento sub-tick
> (`changes/22`). Com alinhamento por tick inteiro eles davam `0,0769` / `0,0691` / `0,0647` —
> e **a variação entre eles era artefato**, só refletia onde a parte fracionária do atraso
> caía. O resíduo real é praticamente o mesmo nas três, que é o que se esperava de três
> corridas nas mesmas condições.

## Soak de 600 s, instância única — saída real do avaliador

## Soak de 600 s — saída real do avaliador

```
PASS  integridade         1 arquivos, 600 amostras, 3 eventos, 42 linha(s) fora do contrato
INFO  procedencia         transporte=local rtt_injetado=0ms perda_injetada=0.0% build=4333f7b  <<< transporte LOCAL: estes numeros NAO valem como 'sobre o relay da Steam'
PASS  duracao             cobertura 600.1 s (minimo 600 s) = span 599.1 + 1 intervalo de 1.00 s
PASS  relogio_coerente    host/0: 50.0 tick/s mediano
PASS  fps_host            99.83% das 600 amostras com fps>=60 e p99<=16.67ms (exigido 99%)
INFO  banda_por_cliente   nenhuma amostra de cliente
PASS  viga_nao_teleporta  maior salto 0.209 u (limite 0.50) em t=129.022 id=0 | 0 amostra(s) acima
FAIL  resposta_do_input   nenhuma amostra de cliente com input_ms_p99=
FAIL  drift               nenhuma amostra de cliente com t>=300s e drift_max_u=
FAIL  late_join           nenhum evento ev=late_join_done
FAIL  queda_do_host       nenhum evento ev=host_quit na corrida
PASS  zero_excecoes       nenhuma linha ev=exception
------------------------------------------------------------------------------
RESULTADO: FAIL - resposta_do_input, drift, late_join, queda_do_host
```

**Os quatro FAIL são todos a mesma coisa: não há cliente.** Esta corrida é de 17/09 e está
mantida por ser a única medição longa de **instância única** — é a linha de base de fps. Os
quatro FAIL dela foram desde então respondidos pela corrida de duas instâncias acima, exceto
`resposta_do_input`.

## Série do soak — 600 amostras

Números **calculados** sobre as 600 linhas `[SOAK]`, não lidos um a um. Os dois extremos
foram abertos e conferidos no log cru.

| métrica | mín | p01 | mediana | p99 | máx |
|---|---|---|---|---|---|
| `fps` | 0.4 | 2466.3 | **3672.9** | — | 3894.8 |
| `frame_p99_ms` | — | — | **0.459** | 1.810 | 2526.962 |
| `carry_jump_u` | — | — | 0.0035 | 0.0406 | **0.2090** |
| `bodies_awake` | 1 | — | 1 | — | 151 |

- **`fps` mediana 3672,9** com orçamento de 16,67 ms. O cenário do briefing (150 corpos +
  4 portadores carregando a viga de 6 m) está muito longe do teto **nesta máquina e neste
  cenário** — sem arte, sem UI, sem áudio, uma cápsula por jogador. É piso de comparação
  entre as três stacks, **não** prova de que o produto roda a 60.
- **Exatamente 1 amostra de 600 fica abaixo de 60 fps**, e é a primeira: `t=0.029 fps=0.4
  frame_p99_ms=2526.962`. É o primeiro quadro do player, que monta cena, 151 corpos e 4
  portadores. O contrato **proíbe** o emissor descartar aquecimento, então ela está no log.
  O portão passa porque 1/600 = 0,17% cabe nos 1% tolerados — ou seja, **passou por
  diluição, não porque o engasgo sumiu**. Numa corrida curta a mesma stack reprova.
- **`carry_jump_u` máximo `0.2090` em `t=129.022`, com `bodies_awake=1`** — a pilha estava
  **dormindo**. O maior salto da viga não veio de colidir com as caixas; veio do próprio
  carregar, numa virada da patrulha. Bate com o que `changes/07` já tinha medido: o atraso
  da viga é transitório e aparece na mudança de direção.

## Carga: a pilha está acordada em 31 das 600 amostras

O gatilho de carga (`decisions/11`) é a viga carregada varrendo a pilha. Medido:
`bodies_awake > 10` em **31 de 600 amostras — 5,2% da corrida**.

**Isto é pouco, e está declarado como limitação do soak atual**: 95% do tempo a candidata é
medida com a pilha dormindo, que é o caso barato. A intensidade tem dial
(`-soakPatrolSteps`) e **não foi varrida** — a corrida de 600 s usou um único valor (200).

## O que esta stack ainda NÃO provou

| métrica do briefing | estado |
|---|---|
| fps ≥ 60 no host | **PASS**, com a ressalva da diluição acima |
| viga não teleporta | **PASS** com rede real (`0.069`), local (`0.209`) e por modelo (`0.208`) |
| não teleporta sob 150 ms / 3% | **FAIL** — `0,689 u` e `0,743 u` no **cliente**, contra limiar `0,5`. Bimodal: uma corrida a 3% não teve salto. `docs/99` 21 |
| resposta < 100 ms | **não medido** — exige personagem predito no cliente, que não existe |
| drift < 0.15 u após 5 min | **FAIL**. Sem RTT: `0,67 u` / alinhado `0,020`. Sob 150 ms: `1,28 u` / alinhado `0,019` a 1% de perda, `0,35`–`0,39` a partir de 3%. `docs/99` 19 e 21 |
| banda média e pico | **não medível nesta stack** — `rx/tx = -1`. FishNet 4.7.3 não expõe contagem de bytes (`docs/99` 17) |
| late join | **PASS** — `151 de 151 corpo(s)` em `134.8 ms`, entrando aos 3 min |
| queda de host limpa | **PASS** — `host_quit` do host, `shutdown clean=1 reason=host_lost` do cliente, zero exceção |
| zero exceções em 10 min | **PASS**, com duas instâncias |

**O transporte FishySteamworks continua sem passar um byte.** Tudo acima foi medido sobre
**Tugboat**, o transporte UDP local. O lobby da Steam subiu de verdade (`changes/06`: criado
em 300 ms, entrada por código em 350 ms), mas lobby é matchmaking e transporte é socket. A
prova provavelmente exige uma 2ª máquina (`decisions/01`).

**A corrida principal acima não tem RTT injetado.** A corrida sob 150 ms / 3% é a da seção
própria, em development build, e os números das duas **não se misturam**.

O motivo de serem duas corridas e não uma, apurado em 18/09: o simulador de
latência do FishNet é público (`TransportManager.LatencySimulator`) mas **só é consultado em
development build** — `TransportManager.cs:1-3` faz `#if UNITY_EDITOR || DEVELOPMENT_BUILD →
#define DEVELOPMENT`, e os três pontos que o usam estão dentro desse bloco. Num build de
release ele aceita configuração e nunca é chamado: configurar e não ter efeito, sem erro.
Como development build não é o mesmo programa (sem stripping, com hooks de profiler), **o fps
de um não é comparável com o do outro** — são duas corridas, não uma. Detalhe em `docs/99` 20.

Os números de rede que existem (`changes/08`) são de um **modelo determinístico**
(`decisions/10`), rotulados `[MODELO]`, com RTT fixo e **sem jitter** — otimistas
exatamente na dimensão que mais importa (`docs/99` item 16).

## Medições anteriores desta stack

| o quê | número | onde |
|---|---|---|
| cápsula: repouso / marcha / pulo | `1.0000 u` / `4.0000 u/s` / `1.1519 u` | `changes/04` |
| pilha de 150 caixas: assenta em | `3.00 s`, física `0.377 ms` médio / `0.823 ms` p99 | `changes/05` |
| lobby Steam: criação / entrada por código | `300 ms` / `350 ms`, ida-e-volta exato | `changes/06` |
| viga: `carry_jump_u` físico (edit mode) | `0.1588` pior caso de 9 corridas | `changes/07` |
| viga: atraso da folga | `70–125 ms` no arranque, `8–24 ms` em regime | `changes/07` |
| viga: agarre assimétrico | inclina 10–13°, folga de `2.3 u` que **não volta** | `changes/07` |
| `[MODELO]` 4 abordagens sob 150 ms/3% | mola `0.2078`; só a abordagem 1 reprova | `changes/08` |
| `[MODELO]` o botão é o buffer, não o RTT | RTT plano de 50 a 400 ms | `changes/08` |
| late join: mundo completo (151) no cliente | `134.8 ms`, entrando aos 3 min | `changes/18` |
| late join: 1º objeto chega no cliente em | `122–135 ms`, em 6 corridas | `changes/12`, `14`, `16`, `17`, `18` |
| viga carregada: velocidade | `0.108 u/tick` de rede (~`3.25 u/s`) | `changes/15` |
| atraso do cliente sobre o host | `4–5 ticks` de rede = `133–167 ms`, RTT zero | `changes/15` |
