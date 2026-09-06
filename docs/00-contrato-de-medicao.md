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
            rtt_ms=<int> loss_pct=<float> bodies=<int> started=<ISO-8601>
```

- `run` — mesmo id nas 4 instâncias da mesma corrida. É a chave de junção.
- `transport` — `local` ou `steam`. **Nenhuma métrica de rede pode ser publicada como
  "sobre Steam" com este campo em `local`** (ver `decisions/01` da task).
- `rtt_ms` / `loss_pct` — o que foi **injetado**, não o observado.
- `build` — SHA curto do commit que gerou o binário. Corrida sem âncora não é evidência.

## 2. Linha de amostra — `[SOAK]`

Uma por segundo, por instância. É a série temporal.

```
[SOAK] t=<float> tick=<int> role=<host|client> id=<0..3>
       fps=<float> frame_p99_ms=<float>
       rx_KBps=<float> tx_KBps=<float>
       drift_max_u=<float> drift_p99_u=<float>
       carry_jump_u=<float> input_ms_p99=<float>
       bodies_awake=<int> world_hash=<hex8>
```

| campo | quem emite | como se calcula |
|---|---|---|
| `fps` | todos | quadros no último segundo. Não é média móvel. |
| `frame_p99_ms` | todos | p99 dos tempos de quadro **do último segundo** |
| `rx_KBps` / `tx_KBps` | todos | bytes de socket no último segundo ÷ 1024 |
| `drift_max_u` | **só cliente** | maior distância entre a posição desta instância e a do host **no mesmo `tick`**, sobre os corpos-sonda. `-1` no host. |
| `drift_p99_u` | **só cliente** | p99 da mesma distância. `-1` no host. |
| `carry_jump_u` | todos | maior salto de posição da viga entre dois quadros **consecutivos** no último segundo |
| `input_ms_p99` | **só cliente** | p99 do intervalo entre o input ser lido e o próprio personagem se mover na tela local |
| `bodies_awake` | todos | rigidbodies não adormecidos. É o que explica o pico de banda. |
| `world_hash` | todos | hash de 8 hex das posições dos corpos-sonda **quantizadas a 0.01 u**, no `tick` da linha |

**Por que `bodies_awake` está aqui:** 150 corpos a 30 Hz com pose completa dariam da
ordem de 120 KBps por cliente. Uma pilha assentada dorme e não manda quase nada; uma
pilha desabando manda tudo. Sem esta coluna, "média de banda" é um número que não
significa nada — não se sabe se a pilha estava parada. Não é otimização: é a variável
de controle da medição.

**Por que `world_hash` é quantizado:** float não bate bit a bit entre instâncias, e não
precisa. 0.01 u é uma ordem de grandeza abaixo do limiar de drift do briefing (0.15 u),
então o hash detecta estado errado sem acusar ruído numérico.

## 3. Linha de evento — `[SOAK-EV]`

Aperiódica. É o que o PASS/FAIL de late join e de queda do host lê.

```
[SOAK-EV] t=<float> tick=<int> role=<...> id=<...> ev=<nome> [campos extras]
```

| `ev` | quando | campos extras |
|---|---|---|
| `lobby_created` | host criou o lobby | `lobby=<id64> code=<str>` |
| `lobby_joined` | cliente entrou | `via=<invite\|code>` |
| `spawn_done` | os 150 corpos existem | `bodies=<int>` |
| `late_join_begin` | instância entra depois do início | `at_t=<float>` |
| `late_join_done` | estado recebido por inteiro | `elapsed_ms=<float> world_hash=<hex8>` |
| `grab` / `release` | agarre da viga | `by=<id> holders=<int>` |
| `host_quit` | host encerrou de propósito | — |
| `shutdown` | instância encerrou | `clean=<0\|1> reason=<str>` |
| `exception` | qualquer exceção não tratada | `where=<str>` |

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
