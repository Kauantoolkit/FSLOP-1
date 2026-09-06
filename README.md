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
spike/A-unity-ngo/               candidata A — Unity 6 + Netcode for GameObjects
spike/C-godot/                   candidata C — Godot 4
harness/                         Fase 2, na stack que vencer. Vazio por ora.
```

## Como rodar o soak

**Estado hoje (06/09/2026): o avaliador existe e está testado; nenhuma stack emite
log ainda.** Então o comando abaixo funciona, mas não há corrida real para apontá-lo.

```
python tools/soak/avaliar.py runs/<id-da-corrida>
```

Ele lê os `.log` das 4 instâncias, aplica os limiares do contrato e imprime uma linha
`PASS`/`FAIL` por métrica. Sai com código 0 se tudo passou, 1 se algo reprovou, 2 se
nem conseguiu ler.

Para conferir que o avaliador está são antes de confiar num veredito:

```
python tools/soak/teste_avaliar.py
```

São 20 casos: um que **deve passar** e dezenove que **devem reprovar**, cada um
quebrando uma métrica de propósito. Se esse teste não fechar 20/20, nenhum resultado
de soak vale.

## A regra que vale para tudo aqui

Quem simula não julga a si mesmo. As stacks **emitem** texto; o avaliador, que não
conhece Unity nem Godot, **interpreta**. Nenhuma stack calcula o próprio PASS/FAIL,
suaviza série ou descarta outlier.

E, do briefing: nada de "funcionou" sem o número medido ao lado.
