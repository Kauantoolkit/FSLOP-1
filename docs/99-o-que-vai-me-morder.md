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
constante única, então é troca de uma linha. Detalhe em `decisions/02`.

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

## 5. A abordagem de agarre da viga não está escolhida — e não é minha para escolher

**Estado:** deliberadamente aberto. É o único item desta lista que é **design**.

Joint entre um corpo predito no cliente e um corpo replicado do host liga dois relógios
diferentes. As três saídas conhecidas (suspender a predição durante o agarre; predizer
o par e reconciliar; agarre cinemático em grid) trocam responsividade por estabilidade
em proporções diferentes, e o briefing diz: *"Se uma decisão depender de julgamento
sobre o que é divertido: PARE e me pergunte. Você não decide design."*

**Sai daqui quando:** a change 08 medir as três na stack B e o usuário escolher com os
números na mão.

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
`FishNet.Transporting`. A deriva de 2 anos não quebrou a compilação. Se quebra em
runtime, quem responde é a change 06 (lobby). O item continua aberto por isso.

**Sai daqui quando:** o lobby Steam da change 06 subir e fechar conexão de verdade.
Detalhe em `tasks/FSLOP-1/decisions/06`.

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
