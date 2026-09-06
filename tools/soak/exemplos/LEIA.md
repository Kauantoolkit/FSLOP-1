# Exemplos

`corrida-exemplo/` é uma corrida **sintética** de 2 instâncias que passa em tudo.
Ela existe para um humano ver, em texto, o que o contrato
(`docs/00-contrato-de-medicao.md`) pede — não é medição de nada.

```
python ../avaliar.py corrida-exemplo --instancias 2 --duracao-minima 3
```

Os números dentro dela são **inventados**. `build=0000000` está ali de propósito:
nenhuma corrida de verdade tem esse SHA, então nenhum número daqui pode vazar para um
relatório sem alguém perceber.

Os casos que **reprovam** não estão aqui — estão em `../teste_avaliar.py`, que monta
os logs em diretório temporário e afirma qual métrica cada um tem que derrubar. São 20
casos: 1 que deve passar, 19 que devem reprovar. Fixture de 600 amostras em disco não
seria legível nem diffável; o que vale versionar é a afirmação do veredito, não o log.
