# O que NÃO foi resolvido e vai me morder depois

Entregável 4 do briefing. **Escrito desde a primeira change, não montado no fim** —
lista feita no fim é lista do que ainda estava na memória de quem escreveu.

Cada item diz o que é, por que ficou, e o que faria ele sair daqui.

---

## 1. O soak automatizado não atravessa o relay da Steam

**Estado:** por desenho, permanente enquanto houver uma máquina só.

O Steam mantém uma conta logada por PC. Quatro instâncias aqui compartilham o mesmo
SteamID, e dois peers com SteamID igual não formam sessão P2P. Como o briefing exige
que o soak *rode sozinho*, ele roda em transporte **local** com 150 ms e 3% de perda
injetados. O relay fica sem cobertura automatizada.

O `[SOAK-META]` carrega `transport=local|steam`, e o avaliador imprime um aviso quando
todas as instâncias estão em `local` — para nenhum número desta corrida ser publicado
como "medido sobre o relay".

**Sai daqui quando:** existir uma 2ª máquina com conta Steam separada e a prova manual
de relay for executada. Detalhe em `tasks/FSLOP-1/decisions/01`.

## 2. AppID 480 (Spacewar) é público

**Estado:** aceito durante o spike.

No 480, qualquer pessoa que adivinhe o código de sala entra, e o Steam não isola por
build. Serve para teste; não serve para lançamento.

**Sai daqui quando:** houver AppID próprio (US$ 100 no Steamworks). O AppID está numa
constante única (`SteamLobbyService.SpikeAppId`), então é troca de uma linha.
Detalhe em `decisions/02`.

**Atualizado em 06/09 (change 06):** exercitado de verdade — lobby criado como
`FriendsOnly`, e não `Public`, exatamente para não entrar na lista pública compartilhada
do 480. A entrada é por código derivado do SteamID, sem varrer lista nenhuma.

## 3. A VRAM da máquina de referência não foi apurada

**Estado:** aberto, e bloqueia o relatório.

O `AdapterRAM` do WMI é campo de 32 bits e saturou em 4 GB para a RTX 3060 — número
falso. Publicar "RTX 3060 4 GB" como hardware de referência seria publicar um erro.

**Sai daqui quando:** rodar `nvidia-smi` e anotar o valor real em `tools/TOOLCHAIN.md`.

## 4. Medição de fps por Parsec não é reprodutível

**Estado:** **fechado para a candidata B em 17/09/2026**; aberto para A e C.

A máquina tem um Parsec Virtual Display Adapter. Uma corrida feita durante sessão
remota passa por outro caminho de apresentação, e o fps medido não vale como o fps do
monitor local.

O campo `display=local|parsec` **entrou no `[SOAK-META]`** junto com a primeira corrida que
mediu fps de verdade (`changes/09`), e a candidata B o emite. As corridas registradas até
agora são todas `display=local`.

**Sai daqui quando:** as candidatas A e C também emitirem o campo. Antes disso, nenhum fps
delas é comparável com o da B.

## 5. O agarre da viga está medido sem rede — e a rede é a metade que falta

**Estado:** decidido em 06/09/2026, **medido localmente em 17/09/2026** (commit `a6a8836`),
**zero medição com rede**.

Este item era "não está escolhido, e não é meu para escolher". Mudou: em 06/09 o usuário
delegou explicitamente a escolha, e a decisão está em `tasks/FSLOP-1/decisions/09` —
**agarre por mola de mão única**, com a viga simulada só no host, joint elástico criado só
no host e **nenhum joint no cliente**. Foi o próprio usuário quem abriu essa saída, ao
perguntar se os personagens não poderiam simplesmente aplicar forças sobre a viga; ela não
estava entre as três que eu tinha levantado.

A justificativa era, em uma frase: um agarre mole **já atrasa a viga em relação à mão mesmo
em jogo local**, e o atraso da rede se esconde dentro do atraso da física — sem predizer a
viga, sem reconciliá-la, e sem alterar nenhuma restrição do briefing.

**A medição de 17/09 confirmou metade disso.** O agarre é estável: `carry_jump_u` pior caso
`0.1588 u` contra o limiar `0.5`, em 9 corridas, `exit=0`. Mas o atraso físico que ia
servir de esconderijo é **transitório** — 70 a 125 ms no arranque, e **8 a 24 ms em
regime**, porque a viga sai do chão e corpo no ar a velocidade constante não pede força da
mola. **Um engasgo de rede durante o carregar em linha reta vai aparecer.** O esconderijo
existe nas mudanças de direção, que é onde a rede também erra mais — favorável, mas bem
menos do que estava escrito.

**O que continua devendo:**

- **a metade de rede foi respondida por MODELO em 17/09** (`changes/08`, `decisions/10`),
  não por rede: a abordagem escolhida dá `carry_jump_max_u = 0.2078` contra o limiar `0.5`,
  e só a abordagem 1 reprova, por `input_ms`. **Nenhum byte passou por transporte nenhum**
  — ver item 16 para o que o modelo não cobre;
- o orçamento que a rede tem para caber era `0.5 − 0.159 ≈ 0.34 u`, e o modelo diz que ela
  consome `0.11` dele. Sobra folga, **sob as hipóteses do item 16**;
- a mola quase não puxa o jogador de volta. O dial existe e foi medido — `massScale` 20
  corta o atraso de pico para 31 ms mas custa 14% do deslocamento —, **e só tem curso para
  o lado de acoplar mais**: `0.05` é indistinguível de `1.00`. Quanto de retorno é bom
  continua sendo número de sensação, item 9;
- sobra um acoplamento que a escolha não remove: o personagem **predito** colide com a
  viga **replicada**. É a mesma situação das 150 caixas, que o briefing já aceita.

**Sai daqui quando:** a metade de rede da change 08 medir as quatro abordagens sob RTT
emulado e a escolhida fechar `carry_jump_u ≤ 0.5` com `input_ms_p99 ≤ 100`. Se não fechar,
o degrau seguinte já está escrito em `decisions/09`.

## 6. A camada de rede da candidata C não está escolhida

**Estado:** aberto até a Fase 1-C.

GodotSteam como módulo × como GDExtension × `steam-multiplayer-peer`. A compatibilidade
com Godot 4.7.x muda por release e não pode ser afirmada de memória.

**Sai daqui quando:** a Fase 1-C começar e a documentação da versão exata for lida.

## 7. O transporte Steam da candidata B está parado há 2 anos

**Estado:** aberto, e é a maior ameaça à candidata favorita.

Apurado em 06/09/2026 pela API do GitHub:

```
FishNet          tag 4.7.3   ultimo push 2026-09-02
FishySteamworks  tag 4.1.1   commit da tag 2024-08-26
```

A candidata B é a única com predição/reconciliação pronta — que é o que o requisito
mais difícil do briefing precisa. Mas a peça que a liga ao "P2P via relay da Steam" é a
mais abandonada das três stacks, e o `package.json` dela traz `"dependencies": {}`, ou
seja, não declara nem FishNet nem Steamworks.NET.

Detalhe apurado no mesmo momento: as tags mentem sobre a própria versão — a tag `4.7.3`
do FishNet declara `4.7.2`, e a `4.1.1` do FishySteamworks declara `4.1.0`. Não quebra
nada (o UPM instala pela tag), mas a versão que o Unity mostra **não** é a instalada.

**Atualizado no mesmo dia, ao instalar (change 02):** ele **compila limpo** —
`Assembly-CSharp.dll`, 0 `error CS`, com `FishySteamworks` referenciando
`FishNet.Transporting`. A deriva de 2 anos não quebrou a compilação.

**Atualizado em 06/09 (change 06):** o lobby subiu, e o transporte **continua sem
resposta**. A Steam inicializa contra o AppID 480 de dentro do editor headless, um lobby
de 4 lugares é criado nos servidores da Valve em 300 ms, o código de entrada funciona e
entrar por ele responde em 350 ms. **Nada disso passa pelo FishySteamworks:** lobby é
matchmaking, transporte é socket. Nenhum byte trafegou por ele ainda.

**Sai daqui quando:** o transporte abrir um socket e fechar conexão de verdade. Isso
esbarra no item 1 desta lista — duas instâncias nesta máquina têm o mesmo SteamID e não
formam P2P —, então a prova provavelmente exige a 2ª máquina. Detalhe em
`tasks/FSLOP-1/decisions/06` e `changes/06`.

## 7b. O transporte da candidata B não é dependência versionada

**Estado:** aberto, e não tem solução limpa.

FishySteamworks não pode ser instalado por UPM de jeito nenhum: pelo git da raiz chega
**vazio** (o `.gitignore` do repo tem `*/`, que ignora todo diretório, e as exceções
apontam para um caminho que não existe mais na tag); por `?path=` chega completo mas
**não compila**, porque o pacote não tem `.asmdef` e o Unity não compila script de
pacote UPM sem um.

Ficou copiado para `Assets/FishNet/Plugins/FishySteamworks/`. Funciona, mas **não há
pino de versão no `manifest.json`** para ele: atualizar ou auditar vira trabalho
manual, e uma máquina nova só confere comparando arquivo por arquivo. A procedência
está em `spike/B-unity-fishnet/VERSOES.md`.

**Sai daqui quando:** a candidata B vencer o spike e valer a pena fazer um fork com
`.asmdef` — ou quando ela perder e o problema deixar de existir.

## 8. Nenhuma stack emite log ainda

**Estado:** é onde o projeto está, não um problema — mas precisa estar escrito para
ninguém confundir "o avaliador passa em 20/20" com "a base foi medida".

O avaliador está testado contra logs sintéticos. **Nenhum número de stack real existe.**

Atualizado em 06/09 (`changes/04`): a candidata B já produz número — repouso, marcha e
pulo da cápsula, medidos pela `SpikePhysicsProbe`. Mas isso é física local em edit mode.
**Nenhuma linha `[SOAK]` foi emitida por stack nenhuma**, e nenhuma métrica do briefing
(fps, banda, drift, late join, queda do host) foi medida.

## 9. Os números de sensação da cápsula são placeholder, não escolha

**Estado:** aberto, e é design — não é meu para fechar.

O `CapsuleMotor` tem três números que decidem como o personagem *sente*:

```
moveSpeed          4 u/s     ANCORADO — docs/00 deriva o limiar de teleporte dele
jumpHeight         1.2 u     PLACEHOLDER
acceleration      40 u/s²    PLACEHOLDER
```

`moveSpeed` não é livre: o limiar de 0.5 u de salto da viga foi justificado a partir de
"o personagem anda a ~4 u/s". Mudar a velocidade obriga a rever o contrato de medição.

Os outros dois estão ali para a cápsula sair do chão e parar de patinar, não porque
alguém os escolheu. O briefing diz que eu não decido o que é divertido.

**Desde 17/09 a viga trouxe mais três**, e esses são os que mais mudam a sensação, porque
decidem o peso que a mão sente:

```
CarryBeam.massKg           120 kg   PLACEHOLDER — 120 contra os 70 kg do jogador
CarryBeam.spring          4000      PLACEHOLDER — é quem levanta a viga, não a força do jogador
CarryBeam.damper           200      PLACEHOLDER
joint.massScale              1      DIAL medido: 20 corta o atraso a 31 ms e custa 14% do avanço
```

`spring` e `damper` não são afinação: são o agarre. Com `spring` 4000 uma mão sozinha
levanta os 120 kg (ver item 15). Baixar a mola torna a viga pesada e lenta; subir torna o
agarre rígido e devolve o problema de rede que `decisions/09` foi escrita para evitar.

**Sai daqui quando:** o usuário jogar e disser os números — ou disser que não importam
para o spike, o que também é resposta.

## 10. A cápsula não tem atrito nenhum

**Estado:** aceito enquanto o motor controlar a velocidade todo passo.

`decisions/08` põe material sem atrito na cápsula, porque o atrito padrão do PhysX
roubava `mu·g·dt = 0.1177 u/s` da velocidade comandada (`errors/01`) e faria a velocidade
depender da caixa embaixo do pé quando a pilha da change 07 existir.

O preço: **nada freia a cápsula além do próprio motor**. Enquanto ele escreve
`linearVelocity` a cada passo, isso é invisível. Se ela algum dia ficar sem motor —
ragdoll, nocaute, cliente que perdeu a conexão e não recebe mais input — ela desliza sem
parar.

**Sai daqui quando:** existir algum estado em que o personagem é largado para a física,
e aí o material tiver que voltar a ter atrito nesse estado.

## 11. O PhysX não segura pilha solta acima de ~6 camadas

**Estado:** medido em 06/09/2026, e é um limite da stack, não do meu teste.

Quatro proporções de pilha de 150 caixas 1×1 foram simuladas até dormirem. O critério é
`max_desloc_u`: quanto a caixa mais deslocada andou, **sem ninguém encostar nela**.

| forma | camadas | `max_desloc_u` | dormiu em | veredito |
|---|---|---|---|---|
| 5×5×6 | 6 | **0.1447** | 3.00 s | fica de pé |
| 5×3×10 | 10 | 21.1541 | 21.78 s | desaba sozinha |
| 5×2×15 (incl. 0.02) | 15 | 24.6038 | 13.36 s | desaba sozinha |
| 5×2×15 (incl. 0.10) | 15 | 3057.9190 | não dormiu | desaba, e **3 caixas voam** |

O corte está na **altura**, não na largura nem na inclinação. Acima de ~6 camadas o
solver do PhysX com `solverIterations` padrão não segura corpos soltos empilhados: o erro
de cada micro-impacto acumula e a pilha se desmancha. Numa das corridas três caixas foram
ejetadas a 3 km.

**Por que isso importa para o briefing:** "pilha instável" com 150 corpos é o cenário de
carga do teste mínimo. Se a stack não consegue manter uma pilha alta parada, o cenário
de carga tem que ser largo e baixo — e um cenário largo e baixo é justamente o que **um
jogador não consegue derrubar** (medido: 280 N·s, o momento de 70 kg a 4 u/s, moveu 0 de
150 caixas). As duas coisas juntas apertam o desenho do soak.

**O que ainda não foi tentado:** subir `Rigidbody.solverIterations`. Não foi mexido de
propósito — os números acima valem para a configuração **padrão**, que é a única base
justa para comparar com a candidata C. Tunar o solver da B e não da C inventaria uma
vitória.

**Sai daqui quando:** a candidata C (Godot) rodar o mesmo teste e a tabela tiver as duas
colunas. Detalhe do processo em `tasks/FSLOP-1/errors/03`.

## 12. Um jogador sozinho não derruba a pilha

**Estado:** medido, e muda o desenho do soak.

Um empurrão de **280 N·s** — exatamente o momento que a cápsula de 70 kg carrega a 4 u/s,
ou seja, o máximo que um jogador consegue entregar correndo — moveu **0 de 150 caixas**
mais de 0.5 u. A pilha de 1.500 kg absorve o impacto e volta a dormir em 2.44 s.

Isso não é bug: é o que acontece quando 150 caixas de 10 kg estão empacotadas. Mas tem
consequência direta no soak: **inputs scriptados que só esbarram na pilha vão medir banda
de pilha dormindo**, e o `docs/00-contrato-de-medicao.md` avisa que média de banda com
`bodies_awake` baixo não significa nada.

**FECHADO em 17/09/2026** (`changes/09`, `decisions/11`). O gatilho escolhido não estava na
lista de saídas que eu tinha levantado: é **a viga de 6 m carregada pelos 4 portadores
varrendo a área**, com o tamanho do quadrado de patrulha como intensidade.

A escolha saiu de duas corridas de 45 s da mesma build, variando **só** o lado da patrulha,
com a série de `bodies_awake` enumerada inteira:

- **patrulha de 16 u** — a pilha dorme em ~4 s, **acorda em t=26 e de novo em t=41**: a
  viga alcança a pilha na segunda volta e a derruba, repetidamente;
- **patrulha de 2 u** — dorme em ~4 s e **não acorda em 45 s**.

Uma variável, resultados opostos. O gatilho é periódico sem ser roteirizado (a carga volta
porque a patrulha volta), usa o cenário do próprio briefing em vez de um empurrador
sintético, e tem dial sem recompilar (`-soakPatrolSteps`).

**O que continua valendo deste item:** a pilha de 6 camadas é alvenaria e um jogador
sozinho não a derruba. Isso não mudou — mudou o que o soak faz a respeito.

## 13. O código de sala depende de um detalhe interno do SteamID

**Estado:** funcionando e vigiado, não resolvido.

O código de entrada (7 dígitos, ex. `3JJ-S8QT`) carrega só os 32 bits de `accountID` do
lobby e **remonta** o resto do `CSteamID` na leitura — universo, tipo de conta e os bits
de instância. Esse último é `0x60000`, e é um detalhe interno da Valve: o nome óbvio do
SDK, `k_EChatInstanceFlagLobby`, vale `0x40000` e **está errado** para lobby de
matchmaking, que carrega também o flag `MMSLobby` (`errors/04`).

Se a Valve mudar isso, os códigos param de levar a lobby nenhum — **sem erro, sem
exceção, sem log**: o código é gerado, ditado por telefone e simplesmente não abre nada.

**A defesa está montada:** a `SteamLobbyProbe` confere o ida-e-volta contra o `id64` real
em toda corrida, e imprime a instância observada quando falha. Não some o risco; garante
que ele apareça no CI e não na mão do jogador.

**Sai daqui quando:** o código passar a carregar os 64 bits inteiros (13 dígitos em vez
de 7) — o que troca robustez por um código que ninguém dita por voz. É escolha, e não foi
feita.

## 14. `carry_jump_u` sozinho não reprova um agarre ruim

**Estado:** aberto, e é um furo na métrica do briefing — não na implementação.

Medido em 17/09 (`changes/07`). Na corrida em que o agarre é **assimétrico** — as mãos
juntas numa ponta da viga —, o resultado é claramente péssimo: a viga inclina 10,50°, a
ponta livre raspa o chão, e a folga entre mão e viga vai a **2,28 u (570 ms) e nunca mais
volta** (`atraso_regime_u = 2.2656`). Em jogo, é carregar uma viga presa por um elástico.

E `carry_jump_u` nessa corrida **melhora**: `0.0792`, o melhor de todas as 9 — contra
`0.1588` do caso simétrico com 4 mãos, que é o bom. A razão é simples e perversa: a viga
se move **menos**, e `carry_jump_u` mede quanto ela se move entre quadros. Uma viga travada
no chão teria `carry_jump_u = 0` e passaria com nota máxima.

O briefing fixa `carry_jump_u ≤ 0.5` e nada mais sobre o carregar. Do jeito que está, a
métrica **reprova teleporte e aprova arrasto** — e arrasto é o outro jeito de o agarre ficar
ruim.

**A defesa começada:** a `BeamCarryProbe` passou a imprimir `atraso_regime_u` ao lado, que
é exatamente o que separa os dois casos (0,03 no bom, 2,27 no ruim). Falta virar limiar no
`docs/00-contrato-de-medicao.md`, e **o limiar é número de sensação** — quanto de atraso
ainda é "carregar junto" e quanto já é elástico é design, item 9.

**Sai daqui quando:** o contrato de medição ganhar um segundo campo para o carregar, com
limiar escolhido por quem joga.

**Confirmado uma segunda vez, em 18/09, e desta vez em rede** (`errors/07`). O cliente seguia
a viga a **um terço** da velocidade do host, chegando a 10 u de distância — e o
`carry_jump_u` dele estava em `0.0099`, contra `0.0085` do host. Dois números próximos, os
dois pequenos, os dois "razoáveis". A métrica mede **suavidade**, e um objeto que segue liso
a trajetória **errada** passa com folga. Não é mais um furo hipotético: já deixou passar um
defeito real.

## 15. "Carregável por até 4" não é imposto por nada

**Estado:** aberto, e é design — não é meu para fechar.

O briefing pede "1 objeto rígido longo (6 m) carregável por **até 4** jogadores ao mesmo
tempo". Medido em 17/09: **uma mão sozinha levanta os 120 kg** (`subiu_u = 0.9318`,
`altura_pos_agarre = 1.1818`). Mais mãos ajudam pouco — 4 mãos levantam a `1.3510`, ~14% a
mais que uma.

O motivo está no desenho do agarre: quem levanta é a **mola** (`spring` 4000), e a força
dela não escala com quantos jogadores seguram. A massa da viga não é um limite; é só o que
a mola tem que vencer, e ela vence.

Ou seja: hoje "até 4" descreve quantos **cabem**, não quantos são **necessários**. Se o
requisito quer dizer "precisa de 4", isso tem que virar regra explícita — um teto de força
por mão, ou a mola escalando com o número de agarres —, e **quanto** é design.

**Sai daqui quando:** o usuário disser se "até 4" é capacidade ou exigência. Se for
exigência, vira change própria com número dele.

## 16. Os números de rede são de um MODELO sem jitter, e o jitter é o que morde

**Estado:** aberto por construção. É a contrapartida declarada de `decisions/10`.

A tabela das quatro abordagens (`changes/08`) **não saiu de uma rede**. O host roda de
verdade, a trajetória da viga é gravada, e sobre ela se aplica aritmética: amostragem a
20 Hz, atraso fixo, perda com semente fixa, interpolação. Isso responde "o desenho aguenta?"
e **não** responde "o meio aguenta?".

O que o modelo deixa de fora, em ordem de quanto deve doer:

1. **Jitter.** O modelo usa **RTT fixo**. E a varredura mostrou que atraso constante é
   inofensivo — o que produz salto é a *variação*, que é exatamente o que não está aqui.
   Este é o item: **o número publicado é otimista, e otimista justamente na dimensão que
   mais importa.**
2. **Rajada de perda.** A perda do modelo é independente por pacote. Perda real vem em
   rajada, e duas ou três perdas seguidas estouram qualquer buffer. O joelho medido (entre
   10% e 25% de perda independente) vai aparecer **antes** com rajada.
3. **Custo de CPU** de serializar, enviar e aplicar snapshot — zero no modelo.
4. **Tudo que é transporte:** handshake, MTU, reordenação, late join, queda de host. Item 7.

**A defesa que existe:** o modelo é determinístico (semente fixa) e barato de varrer, então
quando a rede real existir, rodar os dois sobre o mesmo cenário dá o **delta entre modelo e
realidade** — que é a medida de quanto o modelo mente, e serve para todas as três stacks.

**Sai daqui quando:** a mesma trajetória for medida com transporte real e o delta for
publicado. Depende da 2ª máquina (`decisions/01`), como o item 7.

## 17. O FishNet 4.7.3 não deixa contar bytes, e banda é campo do briefing

**Estado:** aberto. Lacuna **da stack**, não do teste.

O briefing manda reportar banda média e pico por cliente; o contrato tem `rx_KBps` e
`tx_KBps`. A candidata B **não consegue emitir os dois**, e isso foi lido na versão exata
em uso (17/09):

- `Runtime/Managing/Statistic/NetworkTrafficStatistics.cs` **linha 1** é
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD` — num build de release a classe não existe;
- `Runtime/Editor/NetworkProfiler/NetworkTraffic.cs:10` é `internal class NetworkTraffic`,
  e os campos `InboundTraffic`/`OutboundTraffic` de `BidirectionalNetworkTraffic` também
  são `internal` — inalcançáveis de `Assembly-CSharp` em **qualquer** build.

**Nem um development build resolve**, porque a barreira é de visibilidade de assembly e não
de compilação condicional.

Enquanto isso, `rx/tx` saem `-1` (= não instrumentado, ver `docs/00`), e o avaliador diz
`BANDA NAO INSTRUMENTADA` em vez de calcular média de `-1`.

**A saída conhecida** é um `Transport` decorador que envolva o Tugboat e conte bytes em
`SendToServer`/`SendToClient` e nos handlers de recebimento. O próprio FishNet tem o
`Multipass`, que envolve transportes — então o padrão é suportado, não é gambiarra. Custo:
uma classe que implementa ~20 membros abstratos por delegação, e que precisa ser reescrita a
cada mudança de API do `Transport`.

**Por que isso importa para o spike e não só para a B:** é uma diferença concreta entre as
candidatas. Se Godot/GodotSteam expuser contagem de bytes de graça, isso é ponto na
comparação — e é exatamente o tipo de custo escondido que o spike existe para achar.

**Sai daqui quando:** o transporte decorador existir e uma corrida publicar banda real; ou
quando se decidir que banda fica `[NÃO MEDIDO]` para a B e isso for dito no relatório.


## 18. Cena gerada por código não passa pelos passos que a biblioteca espera do Editor

**Estado:** aberto, e já mordeu **duas vezes seguidas** — vai morder na candidata C.

`decisions/07` decidiu montar a cena do spike por script, e a razão continua boa: 150 corpos
precisam nascer em posição determinística para duas corridas poderem comparar `world_hash`.
O custo não estava escrito, e é este: **tudo que uma biblioteca espera que uma pessoa faça
pelo Inspector ou por um item de menu simplesmente não acontece.**

Os dois casos, os dois na mesma semana, os dois no mesmo componente:

| o que faltou | como se manifestou | como foi achado |
|---|---|---|
| id de cena do `NetworkObject` (`errors/06`) | a viga **não existia** em ponta nenhuma; cliente com hash de conjunto vazio por 28 s | aviso num log — **o do host**, não o do cliente |
| `_componentConfiguration` do `NetworkTransform` (`errors/07`) | a viga existia e seguia a **1/3 da velocidade**, até 10 u errada | só apareceu quando uma segunda série de posição existiu para comparar |

O padrão: **default de biblioteca vira escolha implícita de quem gera a cena**, e nenhuma
dessas escolhas passa por revisão de ninguém. Quem monta pelo Inspector é obrigado a olhar o
campo; quem monta por código nunca vê que ele existe.

E os dois falharam **em silêncio, com números plausíveis**. Nenhum produziu exceção. O
segundo não produziu nem aviso.

**O que fazer na candidata C, antes de medir qualquer coisa:** listar quais passos de
editor/importação a solução de rede escolhida espera, e ou executá-los pelo gerador, ou
declarar no relatório que não foram executados. Não adianta esperar o sintoma: o sintoma
destes dois foi um número razoável.

**Sai daqui quando:** existir, para cada candidata, uma conferência explícita de que a cena
gerada tem o mesmo estado serializado que uma cena montada à mão teria — e essa conferência
rodar junto com o gerador, não na minha cabeça.

## 19. O limiar de 0,15 u de drift pode não caber em NENHUMA stack, e isso é do briefing

**Estado:** aberto. **Não é decisão minha** — o limiar é do briefing, e mexer nele é do usuário.

Medido em 18/09 (`changes/15`), soak de 10 min, late join aos 3 min, Tugboat local, **zero
RTT injetado**, zero exceção, 10.653 pares de tick comparados:

```
drift_mesmo_ntick   max 0.7185 u   p99 0.6820 u   (limite 0.15)
drift_alinhado      0.0769 u       com atraso de 5 tick(s)
```

Os dois números dizem coisas diferentes, e é por isso que são dois:

- **o estado do cliente está certo.** Descontado o atraso, a divergência é **0,077 u**,
  abaixo do limiar. O cliente não está simulando outra coisa;
- **o que reprova é latência.** 5 ticks de rede a 30 Hz = **167 ms**. E `p99 ≈ max` significa
  que isso é **regime**, não pico: o cliente fica permanentemente esse tanto atrás.

### A conta que transforma isso num problema de método

A viga carregada anda a **0,108 u por tick** de rede em média, e **0,134** nos trechos retos
(medido nos `[SOAK-POS]` do host da própria corrida, em dois instantes distintos — 400 s e
600 s — com resultados coincidentes).

Para `drift_mesmo_ntick ≤ 0,15 u` a esse ritmo, o atraso total do cliente teria que ser:

| ritmo | ticks de folga | tempo |
|---|---|---|
| 0,108 u/tick (média) | 1,39 | **46 ms** |
| 0,134 u/tick (reto) | 1,12 | **37 ms** |

O buffer de interpolação mínimo do `NetworkTransform` da candidata B é **1 tick**
(`_interpolation`, `[Range(1, MAX_INTERPOLATION)]`, default 2) — **33 ms sozinho**, a 30 Hz.
Sobram ~4 ms para fila, trânsito e fase de amostragem. Com RTT real de 150 ms, que o briefing
exige simular, não sobra nada.

**A inferência — e ela é inferência, não medição:** isto parece ser propriedade do *par
métrica × velocidade do objeto*, não da candidata B. Qualquer replicação interpolada põe o
cliente ao menos um intervalo de snapshot atrás; a 3,25 u/s, um intervalo já custa ~0,11 u de
0,15 disponíveis. **Só será fato quando as candidatas A e C forem medidas com o mesmo
instrumento** — e é exatamente o tipo de coisa que o spike existe para achar.

### As saídas, e por que nenhuma é minha para escolher

| saída | custo |
|---|---|
| subir o `TickRate` (30 → 60 Hz) | corta o atraso pela metade e **dobra a banda** — que é outra métrica do briefing, e que na candidata B **não é medível** (item 17). Trocar um FAIL por um número que não sei medir |
| baixar `_interpolation` para 1 | 33 ms a menos, ao custo de engasgo visível a cada pacote atrasado. É o dial de `changes/08`, medido: buffer 0 deu 117 quadros congelados de 173 |
| extrapolar no cliente | o cliente passa a **inventar** posição; erra nas mudanças de direção, que é onde a viga carregada mais muda |
| medir drift **alinhado** | seria trocar a régua depois de ver o resultado. Não |
| o objeto carregado andar mais devagar | é **design**, e design não é meu |
| o limiar não ser 0,15 u | é do **briefing**, e briefing não é meu |

**Sai daqui quando:** o usuário disser qual das saídas vale — ou quando as candidatas A e C
mostrarem que uma delas fecha 0,15 u com o mesmo teste mínimo, o que tornaria isto um ponto
contra a B em vez de um furo da métrica.

## 20. Os 150 ms e 3% que o briefing manda simular exigem *development build*

**Estado:** aberto, e obriga a corrida a existir em duas versões.

O briefing exige: *"Sob 150ms RTT e 3% packet loss simulados: objeto carregado não teleporta e
o personagem responde em menos de 100ms percebidos"*. **Todas as corridas com rede real feitas
até 18/09 têm RTT injetado ZERO** — e o drift já reprova assim (`docs/99` 19).

O FishNet tem o simulador embutido, e ele é **público**: `TransportManager.LatencySimulator`,
com `SetEnabled`, `SetLatency`, `SetPacketLoss` (`LatencySimulator.cs:67, 99, 140`). O
problema é onde ele é chamado. Lido em `TransportManager.cs`, linhas 1-3:

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
#define DEVELOPMENT
#endif
```

e os três pontos que de fato usam o simulador (`AddOutgoing` no caminho do servidor, o mesmo
no do cliente, e `IterateOutgoing`) estão todos dentro de `#if DEVELOPMENT`. Num build de
release o simulador existe, aceita configuração, e **nunca é consultado** — pior forma de
falhar: configurar e não ter efeito, sem erro.

**Isto é diferente do item 17, e a diferença importa.** Lá (banda) a barreira é de
**visibilidade de assembly** — `internal` ao `FishNet.Runtime` —, e por isso nem development
build resolve. Aqui é **símbolo de compilação**: um development build resolve.

**O preço, e é ele que cria o problema:** development build não é o mesmo programa. Sem
stripping, com hooks de profiler, com verificações extras. **O `fps` de um dev build não é
comparável com o de um build de release**, e fps é outra métrica do briefing. As duas não
cabem na mesma corrida.

**O encaminhamento, que ainda não foi executado:** duas versões declaradas no `[SOAK-META]` —
`build_flavor=release` para fps e teleporte, `build_flavor=development` para as métricas sob
RTT/perda. Nenhum número de uma pode ser citado como se fosse da outra, e o relatório da Fase 1
precisa dizer de qual veio cada linha.

**Sai daqui quando:** o `SpikePlayerBuilder` souber construir as duas, o `[SOAK-META]` carregar
qual é, e existir uma corrida sob 150 ms / 3% com número publicado.
