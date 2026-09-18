# FSLOP-1 — base técnica para protótipos co-op de física

**Não é um jogo. Não é uma engine.** É a fundação reutilizável em cima da qual
protótipos co-op de física ("friendslop") são montados em ciclos de 3-4 meses.

O briefing que define o escopo, as métricas e as proibições está em
`../../tasks/FSLOP-1/briefing-original.md`, transcrito sem edição. **Ele é a fonte de
verdade** — nada aqui o reescreve de memória.

## Onde as coisas estão

```
docs/00-contrato-de-medicao.md   o formato de log que as 3 candidatas emitem
docs/99-o-que-vai-me-morder.md   o que NÃO está resolvido (entregável 4)
tools/soak/avaliar.py            o avaliador PASS/FAIL — externo às stacks
tools/soak/teste_avaliar.py      o teste do avaliador
tools/TOOLCHAIN.md               versões exatas e de onde vêm
spike/B-unity-fishnet/           candidata B — Unity 6 + FishNet
spike/B-unity-fishnet/RESULTADOS.md   os números medidos da B
spike/A-unity-ngo/               candidata A — Unity 6 + NGO. NÃO COMEÇADA
spike/C-godot/                   candidata C — Godot 4. NÃO COMEÇADA
harness/                         Fase 2, na stack que vencer. Vazio por ora.
```

**A recomendação da Fase 1 não existe e não pode existir ainda.** O briefing pede para
recomendar uma das três *pelos números*, e só uma tem números. Tudo que está medido hoje é
sobre a candidata B — e uma das coisas que ela mediu (`docs/99` item 19) só vira conclusão
quando houver com o que comparar.

## Como rodar o soak

**Estado em 18/09/2026:** a candidata B roda com **duas instâncias**, replica os 151 corpos e
é avaliada ponta a ponta. Resultados em `spike/B-unity-fishnet/RESULTADOS.md`. As candidatas
A e C ainda não existem.

### 1. Gerar a cena e construir o player (candidata B)

A cena é **produto de script** — editá-la pelo Editor sem portar a alteração para o gerador
significa perdê-la na próxima geração (`tasks/FSLOP-1/decisions/07`).

```
UNITY="C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe"
PROJ=spike/B-unity-fishnet/FslopSpikeB

"$UNITY" -projectPath "$PROJ" -batchmode -quit -nographics -logFile cena.log \
    -executeMethod Fslop.SpikeB.EditorTools.SpikeSceneBuilder.Build

"$UNITY" -projectPath "$PROJ" -batchmode -quit -nographics -logFile player.log \
    -executeMethod Fslop.SpikeB.EditorTools.SpikePlayerBuilder.Build
```

Para medir **sob RTT e perda**, construa também a versão de desenvolvimento — e leia o porquê
em `docs/99-o-que-vai-me-morder.md` item 20 antes de usar os números dela:

```
"$UNITY" ... -executeMethod Fslop.SpikeB.EditorTools.SpikePlayerBuilder.BuildDevelopment
```

### 2. Rodar as duas instâncias

O host sobe primeiro; o cliente entra depois (é assim que o late join de 3 min do briefing é
exercido). O host com `-soakSeconds` **menor** que o do cliente **é** o teste de queda de host.

```
EXE=$PROJ/Build/SpikeB/FslopSpikeB.exe        # ou Build/SpikeB-dev para RTT/perda

$EXE -batchmode -nographics -logFile runs/x/inst0-host.log \
     -soakRun x -soakBuild <sha> -soakDisplay local \
     -soakRole host -soakId 0 -soakSeconds 660 &
sleep 180
$EXE -batchmode -nographics -logFile runs/x/inst1-client.log \
     -soakRun x -soakBuild <sha> -soakDisplay local \
     -soakRole client -soakId 1 -soakSeconds 600
```

Opções que valem saber:

| flag | o que faz |
|---|---|
| `-soakRtt <ms>` | RTT de **ida-e-volta** a injetar. Exige build de desenvolvimento |
| `-soakLoss <pct>` | perda de pacote em porcento. Idem |
| `-soakPatrolSteps <n>` | tamanho da patrulha da viga — é o dial de carga (`decisions/11`) |
| `-soakDisplay local\|parsec` | de onde o fps foi medido. **Obrigatório** para o número valer |

### 3. Avaliar

```
python tools/soak/avaliar.py runs/x --instancias 2 --duracao-minima 600
```

Lê os `.log` das instâncias, aplica os limiares do contrato e imprime uma linha `PASS`/`FAIL`
por métrica. Sai com código 0 se tudo passou, 1 se algo reprovou, 2 se nem conseguiu ler.

Para conferir que o avaliador está são **antes** de confiar num veredito:

```
python tools/soak/teste_avaliar.py
```

São **32 casos**. A maioria quebra uma métrica de propósito e exige que o avaliador aponte
**aquela** e não outra; alguns fazem o inverso e exigem que ele **não** reprove — porque um
avaliador que reprova tudo passaria em todos os testes do primeiro tipo. Se o teste não
fechar 32/32, nenhum resultado de soak vale.

## A regra que vale para tudo aqui

Quem simula não julga a si mesmo. As stacks **emitem** texto; o avaliador, que não
conhece Unity nem Godot, **interpreta**. Nenhuma stack calcula o próprio PASS/FAIL,
suaviza série ou descarta outlier.

E, do briefing: nada de "funcionou" sem o número medido ao lado.
