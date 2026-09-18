# Contrato de medição

Uma stack candidata **emite**. O avaliador **interpreta**. Nunca o contrário.

Este arquivo é o único acordo entre as três candidatas do spike. Se uma delas não
conseguir emitir um campo daqui, isso é **resultado do spike** — vai para a tabela como
lacuna daquela stack, e não vira campo opcional.

Todas as linhas vão para **stdout**, uma por linha, sem quebra, em ASCII.
Uma instância = um arquivo de log. Quatro instâncias = quatro arquivos.

---

## Unidades, fixadas de uma vez

| símbolo | significa | observação |
|---|---|---|
| `u` | unidade do motor | **1 u = 1 metro**, nas duas engines. Declarado, não presumido. |
| `ms` | milissegundo | ponto decimal, nunca vírgula |
| `KBps` | kilobyte por segundo | **1 KB = 1024 bytes** |
| `t` | segundos desde o início da corrida | float, 3 casas |
| `tick` | número do passo de simulação | inteiro, **o mesmo número no host e nos clientes** |

O `tick` é o que torna o log comparável entre instâncias. Sem um contador de passo
compartilhado não há como medir drift: comparar a posição do host em um instante com a
do cliente em outro mede latência, não divergência. Toda stack candidata precisa expor
esse contador; se não expuser, é uma reprovação da stack, não do teste.

---

## 1. Linha de cabeçalho — `[SOAK-META]`

Exatamente uma por instância, a primeira linha do log. Torna o arquivo
auto-descritivo: quem o ler daqui a três meses sabe contra o que ele vale.

```
[SOAK-META] run=<id> stack=<A|B|C> role=<host|client> id=<0..3> pid=<int>
            build=<sha-curto> engine=<versao> transport=<local|steam>
            display=<local|parsec> rtt_ms=<int> loss_pct=<float>
            bodies=<int> started=<ISO-8601>
```

- `run` — mesmo id nas 4 instâncias da mesma corrida. É a chave de junção.
- `transport` — `local` ou `steam`. **Nenhuma métrica de rede pode ser publicada como
  "sobre Steam" com este campo em `local`** (ver `decisions/01` da task).
- `display` — **acrescentado em 17/09/2026**, ao emitir a primeira corrida com fps de
  verdade. A máquina de referência tem um **Parsec Virtual Display Adapter**, e fps medido
  por sessão remota não é o mesmo número que fps no monitor local. Sem este campo, nenhuma
  medição de fps é reprodutível — foi a pendência registrada em `docs/99` item 4 desde o
  início do spike.
- `rtt_ms` / `loss_pct` — o que foi **injetado**, não o observado.
- `build` — SHA curto do commit que gerou o binário. Corrida sem âncora não é evidência.

## 2. Linha de amostra — `[SOAK]`

Uma por segundo, por instância. É a série temporal.

```
[SOAK] t=<float> tick=<int> ntick=<int> role=<host|client> id=<0..3>
       fps=<float> frame_p99_ms=<float>
       rx_KBps=<float> tx_KBps=<float>
       drift_max_u=<float> drift_p99_u=<float>
       carry_jump_u=<float> input_ms_p99=<float>
       bodies_awake=<int> world_hash=<hex8>
```

| campo | quem emite | como se calcula |
|---|---|---|
| `tick` | todos | contador **local** de passo de física. Começa em zero quando o processo sobe, e por isso **não é comparável entre instâncias**. |
| `ntick` | todos | tick **da rede**, ou `-1` quando não há rede. É o único da linha que serve para casar instâncias. |
| `fps` | todos | quadros no último segundo. Não é média móvel. |
| `frame_p99_ms` | todos | p99 dos tempos de quadro **do último segundo** |
| `rx_KBps` / `tx_KBps` | todos | bytes de socket no último segundo ÷ 1024 |
| `drift_max_u` | **ninguém** | `-1` nos dois papéis. Ver "drift não cabe na linha de amostra", abaixo. |
| `drift_p99_u` | **ninguém** | idem |
| `carry_jump_u` | todos | maior salto de posição da viga entre dois quadros **consecutivos** no último segundo |
| `input_ms_p99` | **só cliente** | p99 do intervalo entre o input ser lido e o próprio personagem se mover na tela local |
| `bodies_awake` | todos | rigidbodies não adormecidos. É o que explica o pico de banda. |
| `world_hash` | todos | hash de 8 hex das posições dos corpos-sonda **quantizadas a 0.01 u**, no `tick` da linha |

**Por que `bodies_awake` está aqui:** 150 corpos a 30 Hz com pose completa dariam da
ordem de 120 KBps por cliente. Uma pilha assentada dorme e não manda quase nada; uma
pilha desabando manda tudo. Sem esta coluna, "média de banda" é um número que não
significa nada — não se sabe se a pilha estava parada. Não é otimização: é a variável
de controle da medição.

### `-1` significa NÃO INSTRUMENTADO, nunca zero

Regra geral, escrita em 17/09 depois de ela ser violada. O contrato já usava `-1` para
"não se aplica" (drift e input no host). A regra completa é:

- **`-1` em qualquer campo numérico quer dizer "esta instância não mede isto".** Nunca
  quer dizer zero, nem quer dizer bom;
- **toda checagem do avaliador tem que filtrar `-1` antes de comparar com limiar.** Sem
  isso, um campo **não medido** passa com folga — foi o que aconteceu: um cliente emitindo
  `input_ms_p99=-1` produzia `PASS  resposta_do_input  pior p99 -1.0 ms (limite 100)`. O
  juiz dando verde exatamente para quem não foi medido;
- **campo que a stack não consegue emitir é lacuna daquela stack**, e aparece no relatório
  como lacuna — não vira campo opcional nem valor inventado.

**Lacuna conhecida da candidata B:** `rx_KBps`/`tx_KBps` saem `-1`. O FishNet 4.7.3 não
expõe contagem de bytes de socket: `NetworkTrafficStatistics.cs` inteiro está sob
`#if UNITY_EDITOR || DEVELOPMENT_BUILD`, e as classes que guardam os contadores
(`NetworkTraffic`, e os campos `InboundTraffic`/`OutboundTraffic` de
`BidirectionalNetworkTraffic`) são `internal` ao assembly `FishNet.Runtime`. Medir banda
nessa stack exige um `Transport` decorador que conte na passagem.

**Por que `world_hash` é quantizado:** float não bate bit a bit entre instâncias, e não
precisa. 0.01 u é uma ordem de grandeza abaixo do limiar de drift do briefing (0.15 u),
então o hash detecta estado errado sem acusar ruído numérico.

### Drift não cabe na linha de amostra — e a versão antiga deste contrato estava errada

**Corrigido em 18/09/2026.** Até aqui esta tabela dizia que `drift_max_u` era emitido
"**só cliente**", definido como a distância entre a posição dele e a do host "no mesmo
`tick`". **Isso é impossível de calcular dentro do cliente**: no instante em que ele emite a
amostra, ele não conhece — e não pode conhecer — a posição autoritativa do host naquele
tick. Tudo que ele tem é o que chegou pela rede, que é justamente o que se quer auditar.
Medir drift contra o que a rede entregou é medir a rede contra ela mesma.

Drift é, por natureza, uma medida **entre duas instâncias**, e por isso ela sai do emissor e
vai para o avaliador, que tem os dois logs. Os dois campos ficam na linha de amostra, com
`-1` nos dois papéis, porque `-1` já quer dizer exatamente isto: *esta instância não mede
isto*.

## 3. Linha de posição — `[SOAK-POS]`

Uma por tick de rede, por instância, **enquanto o corpo observado existir**. É a matéria-prima
do drift e o único par de séries que as duas pontas produzem sobre a mesma coisa.

```
[SOAK-POS] ntick=<uint> t=<float> role=<host|client> id=<0..3> obj=<int> x=<float> y=<float> z=<float>
```

| campo | o que é |
|---|---|
| `ntick` | tick **da rede**, não o `tick` local das outras linhas. Metade da chave do cruzamento. |
| `t` | segundos desde o início **desta** instância. Serve só para o corte dos 5 min. |
| `obj` | id do corpo **em rede**, igual nas duas pontas. A outra metade da chave. |
| `x` `y` `z` | posição do corpo, 4 casas (0,1 mm — o limiar do briefing é 0,15 u) |

**Por que `obj`, e por que não o nome.** O cliente recebe *clones* do prefab, e nome não é
sincronizado — `Box_042` no host é `Box(Clone)` no cliente. Posição na lista também não
serve: a ordem de chegada no cliente não é a de criação no host. O que sobra, e o que a
biblioteca garante igual nas duas pontas, é o id de objeto em rede.

### Cadência: a viga todo tick, os 151 corpos a cada segundo

O corpo carregado é o que o briefing cita nominalmente, e é o que se move o tempo todo —
esse sai **a cada tick**. Os demais saem numa varredura a cada **30 ticks de rede**.

A cadência da varredura é contada em **tick de rede**, nunca em segundos, e isso é o ponto:
assim as duas pontas emitem exatamente nos **mesmos** ticks. Se cada uma amostrasse no
próprio relógio, as duas séries não teriam par nenhum para cruzar e o drift dos 150 corpos
simplesmente não existiria como número.

Varrer 151 corpos a cada tick daria ~4500 linhas/s por instância, e o log viraria o gargalo
da medição — o instrumento passaria a medir a si mesmo.

**O corte dos 5 min usa o `t` do HOST, nunca o do cliente.** O briefing pede drift "após 5
min de simulação contínua", e para um cliente que entrou atrasado o `t` local dele não diz
há quanto tempo o mundo simula — um cliente aos 60 s do próprio relógio pode estar olhando
um mundo de 6 min. O avaliador casa `ntick` → `t` do host e corta por ali.

**Por que `ntick` e não o `tick` das linhas `[SOAK]`.** O `tick` das outras linhas é um
contador local que começa em zero quando **o processo** sobe, e as instâncias sobem em
instantes diferentes — casar por ele compararia momentos diferentes da simulação. O tick de
rede é o relógio do servidor, e é o único eixo comum.

**A ressalva que precisa sobreviver até o relatório.** No cliente, esse tick é uma
*aproximação* do tick do servidor e pode subir **e descer** conforme o timing se ajusta
(candidata B: `TimeManager.cs:126`). O eixo da comparação tem erro próprio. A 30 Hz um tick
vale 33 ms; com a viga a ~0,5 u/s isso dá ~0,017 u de erro de eixo contra o limite de 0,15 u
— uma ordem de grandeza abaixo, mas não zero. Todo número de drift sai acompanhado do
`TickRate` da corrida.

**O que o avaliador extrai das duas séries, e são dois números, não um:**

| número | o que é | por que separado |
|---|---|---|
| `drift_mesmo_ntick` | distância entre host e cliente no **mesmo** `ntick`, por corpo | é o erro de posição que uma pessoa veria na tela: inclui o atraso do buffer de interpolação |
| `drift_alinhado` + `atraso_ticks` | menor distância ao deslocar a série do cliente em até ±N ticks, e de quanto foi o deslocamento | separa **atraso** de **divergência**. Um cliente 3 ticks atrás mas perfeitamente correto não é a mesma falha que um cliente no tick certo e no lugar errado |

**O deslocamento é o mesmo para todos os corpos.** Atraso é propriedade da conexão, não de
cada objeto. Procurar um deslocamento por corpo deixaria cada um escolher o que mais o
favorece, e o número resultante não descreveria nada.

**O pior caso diz qual corpo e quando.** `0,68 u` sozinho não distingue "a viga está atrasada"
de "uma caixa caiu da pilha só no cliente" — e essas duas coisas se investigam em lugares
diferentes.

O limiar de 0,15 u do briefing é aplicado ao **`drift_mesmo_ntick`**, porque é ele que
descreve o que se vê. O `drift_alinhado` não tem limiar: ele é diagnóstico, e existe para
que um FAIL possa ser explicado em vez de só anunciado.

## 4. Linha de evento — `[SOAK-EV]`

Aperiódica. É o que o PASS/FAIL de late join e de queda do host lê.

```
[SOAK-EV] t=<float> tick=<int> role=<...> id=<...> ev=<nome> [campos extras]
```

| `ev` | quando | campos extras |
|---|---|---|
| `lobby_created` | host criou o lobby | `lobby=<id64> code=<str>` |
| `lobby_joined` | cliente entrou | `via=<invite\|code>` |
| `transport_up` | o transporte subiu nesta instância | `papel=<host\|client> transporte=<nome> endereco=<str> porta=<int>` |
| `peer_connected` | um par conectou (no servidor) ou o socket abriu (no cliente) | `conn=<id\|eu>` |
| `peer_disconnected` | o simétrico do acima | `conn=<id\|eu>` |
| `spawn_done` | os 150 corpos existem | `bodies=<int>` |
| `late_join_begin` | instância entra depois do início | `at_t=<float>` |
| `late_join_done` | estado recebido por inteiro | `elapsed_ms=<float> ntick=<int> world_hash=<hex8> bodies=<int>` |
| `grab` / `release` | agarre da viga | `by=<id> holders=<int>` |
| `host_quit` | host encerrou de propósito | — |
| `shutdown` | instância encerrou | `clean=<0\|1> reason=<str>` |
| `exception` | qualquer exceção não tratada | `where=<str>` |

### Late join pergunta "recebeu TUDO?", e só isso

**Corrigido em 18/09/2026.** A checagem de late join comparava o `world_hash` do cliente com o
de uma amostra do host **no mesmo `tick`**, e era insalubre por dois motivos independentes:

1. casava pelo `tick` **local**, que não é comparável entre processos — reprovava com
   "nenhuma amostra do host no tick=5" mesmo quando tudo estava certo;
2. **mesmo com a chave certa, o hash nunca bateria.** O cliente renderiza interpolado, alguns
   ticks atrás do host (medido: **5 ticks**). O hash quantiza a 0,01 u e a viga carregada anda
   ~0,108 u por tick — **um único tick de diferença já muda o hash**. Exigir igualdade num
   instante é exigir latência zero.

Era, portanto, uma checagem que reprovava replicação correta e não tinha como passar.

O briefing pede que o cliente "recebe estado completo e correto". As duas palavras são duas
perguntas, e agora cada uma tem seu dono:

| pergunta | quem responde | como |
|---|---|---|
| **completo?** | `late_join` | o `bodies` do `late_join_done` do cliente contra o `bodies` do `spawn_done` do host. Imune a atraso. |
| **correto?** | `drift` | as duas séries `[SOAK-POS]`, já separando atraso de divergência |

Uma única linha `ev=exception` reprova a corrida inteira. Ela existe para o log
**dizer** o que aconteceu, não para o avaliador ter que adivinhar por regex sobre o
texto de stack trace de duas engines diferentes.

---

## Os limiares, tirados do briefing e transformados em número

O briefing diz "60fps estáveis". "Estável" não é número, e "parece ok é reprovado" vale
também para mim. Fixado aqui, e é isto que o avaliador aplica:

| métrica | regra de PASS | origem |
|---|---|---|
| fps do host | em ≥ **99%** das amostras do host, as duas coisas ao mesmo tempo: `fps` ≥ **60** **e** `frame_p99_ms` ≤ **16.67** | briefing: "60fps estáveis no host" |
| banda | reporta média e pico; **sem limiar de reprovação** | o briefing pede *reportar*, não aprovar |
| teleporte da viga | `carry_jump_u` ≤ **0.5** em todas as amostras | briefing: "não teleporta" |
| resposta do input | `input_ms_p99` ≤ **100** | briefing: "menos de 100ms percebidos" |
| drift | `drift_max_u` < **0.15** em toda amostra com `t` ≥ 300 | briefing: "abaixo de 0.15 após 5 min" |
| late join | existe `late_join_done` **e** seu `world_hash` == o do host no mesmo tick | briefing: "estado completo e correto" |
| queda do host | após `host_quit`, todo cliente emite `shutdown clean=1` | briefing: "mensagem limpa, sem exceção" |
| exceções | **zero** linhas `ev=exception` na corrida | briefing: "zero exceção em soak de 10 min" |
| duração | a corrida cobre ≥ **600 s** | briefing: soak de 10 minutos |

**O limiar de 0.5 u para teleporte é meu, não do briefing, e é declarado como tal.**
Justificativa: o personagem anda a ~4 u/s, então a viga percorre ~0.067 u entre quadros
a 60 Hz. 0.5 u é ~7,5× o deslocamento normal máximo — grande demais para ser movimento,
pequeno o bastante para pegar um salto visível. Se o usuário quiser outro número, muda
aqui e o avaliador segue, sem tocar em nenhuma stack.

**A banda não reprova de propósito.** O briefing manda *reportar* média e pico e não dá
teto. Inventar um teto seria eu decidindo o que é aceitável — e depois "otimizar antes
de ter medição", que é proibido.

---

## Regra que vale para as três candidatas

O log é escrito por quem simula, com o dado que a instância **tem**. Nenhuma stack pode
calcular o próprio PASS/FAIL nem suavizar série (média móvel, descarte de outlier,
"warm-up ignorado"). O avaliador é externo justamente para que o julgamento não more
dentro do que está sendo julgado.
