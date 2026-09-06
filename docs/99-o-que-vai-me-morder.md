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

**Estado:** aberto, e o contrato ainda não protege contra isso.

A máquina tem um Parsec Virtual Display Adapter. Uma corrida feita durante sessão
remota passa por outro caminho de apresentação, e o fps medido não vale como o fps do
monitor local. O `[SOAK-META]` **não tem campo para isso hoje**.

**Sai daqui quando:** o contrato ganhar um campo `display=local|parsec` e a emissão nas
stacks preenchê-lo. Antes da primeira medição de fps que conte como resultado.

## 5. O agarre da viga está escolhido, e ainda não foi medido

**Estado:** decidido em 06/09/2026, **zero medição**.

Este item era "não está escolhido, e não é meu para escolher". Mudou: em 06/09 o usuário
delegou explicitamente a escolha, e a decisão está em `tasks/FSLOP-1/decisions/09` —
**agarre por mola de mão única**, com a viga simulada só no host, joint elástico criado só
no host e **nenhum joint no cliente**. Foi o próprio usuário quem abriu essa saída, ao
perguntar se os personagens não poderiam simplesmente aplicar forças sobre a viga; ela não
estava entre as três que eu tinha levantado.

Por que funciona, em uma frase: um agarre mole **já atrasa a viga em relação à mão mesmo
em jogo local**, e o atraso da rede se esconde dentro do atraso da física — sem predizer a
viga, sem reconciliá-la, e sem alterar nenhuma restrição do briefing.

**O que continua devendo:**

- **nenhum número.** Nenhuma viga existe, nenhum joint foi criado, nenhuma corrida mediu
  `carry_jump_u`. A decisão diz por onde começar;
- a mola quase não puxa o jogador de volta — ele não é arrastado nem levantado pela viga.
  Quanto de retorno é bom é número de sensação, e está no item 9;
- sobra um acoplamento que a escolha não remove: o personagem **predito** colide com a
  viga **replicada**. É a mesma situação das 150 caixas, que o briefing já aceita.

**Sai daqui quando:** a change 08 medir as quatro abordagens na stack B e a escolhida
fechar `carry_jump_u ≤ 0.5` com `input_ms_p99 ≤ 100`. Se não fechar, o degrau seguinte já
está escrito em `decisions/09`.

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

**Sai daqui quando:** a change 09 definir o gatilho de carga do soak. As saídas conhecidas
são: os 4 jogadores empurrarem juntos, largar a viga de 6 m em cima da pilha, ou a pilha
nascer no ar e cair durante a corrida. Nenhuma foi escolhida — e a última é a única que
não depende de nada que ainda não existe.

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

