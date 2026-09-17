# Candidata B — Unity 6 + FishNet + FishySteamworks

Resultados medidos. **Nenhum número aqui foi estimado**; cada um veio de uma corrida com
saída registrada em `tasks/FSLOP-1/changes/`.

## Âncora

| | |
|---|---|
| Repositório | `projects/FSLOP-1`, branch `main` |
| Build medida | `4333f7b` |
| Data | 17/09/2026 |
| Engine | Unity `6000.6.0f1` |
| FishNet | `4.7.3` · Steamworks.NET `2025.164.1` · FishySteamworks `4.1.1` |
| Máquina | Ryzen 5 5600 (6C/12T) · 15,9 GB · RTX 3060 (12113 MB, lido do log do player) · Windows 11 |
| Display | `local` (monitor físico, **não** sessão Parsec) |

## O veredito, em uma linha

**A candidata B passa em tudo que uma corrida de uma instância pode provar, e não provou
nada de rede.** O soak de 10 min fecha fps, teleporte da viga, coerência de relógio e zero
exceções. Drift, `input_ms_p99`, late join e queda de host **exigem uma segunda instância
que ainda não existe**.

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

**Os quatro FAIL são todos a mesma coisa: não há cliente.** Não são defeitos da stack; são
a forma do buraco que falta preencher, e o buraco depende do transporte
(`docs/99-o-que-vai-me-morder.md` item 7).

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
| viga não teleporta | **PASS** local (`0.209`) e **por modelo** sob rede (`0.208`) — nunca com rede real |
| resposta < 100 ms | **não medido** — exige cliente |
| drift < 0.15 u após 5 min | **não medido** — exige cliente |
| banda média e pico | **não medido** — `rx/tx = 0.0` porque nenhum byte atravessou socket nenhum |
| late join | **não medido** |
| queda de host limpa | **não medido** |
| zero exceções em 10 min | **PASS** |

**O transporte FishySteamworks nunca passou um byte.** O lobby da Steam subiu de verdade
(`changes/06`: criado em 300 ms, entrada por código em 350 ms), mas lobby é matchmaking e
transporte é socket. A prova provavelmente exige uma 2ª máquina (`decisions/01`).

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
