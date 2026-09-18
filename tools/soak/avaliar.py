#!/usr/bin/env python3
"""
Avaliador do soak — le os logs de uma corrida e imprime PASS/FAIL por metrica.

Externo de proposito: nao conhece Unity nem Godot, so le texto que segue
docs/00-contrato-de-medicao.md. Quem simula nao julga a si mesmo.

Uso:
    python avaliar.py <diretorio-com-os-logs>
    python avaliar.py <diretorio> --instancias 2 --duracao-minima 5

Saida: uma linha por metrica. Codigo de saida 0 se tudo passou, 1 se algo
reprovou, 2 se os logs nem puderam ser lidos.

Stdlib apenas. Nenhuma dependencia, nenhum instalador.
"""

import argparse
import math
import os
import sys

TAG_META = "[SOAK-META]"
TAG_SAMPLE = "[SOAK]"
TAG_EVENT = "[SOAK-EV]"
TAG_POS = "[SOAK-POS]"

# Ate quantos ticks de rede deslocar a serie do cliente ao procurar o alinhamento
# que separa ATRASO de DIVERGENCIA. 30 ticks a 30 Hz = 1 s, muito alem de qualquer
# buffer de interpolacao razoavel: se o melhor alinhamento encostar neste teto, o
# numero nao e "atraso", e outra coisa — e o avaliador diz isso em vez de reportar
# um minimo que so existe porque a busca acabou.
BUSCA_ATRASO_TICKS = 30

# Limiares. Todos vem de docs/00-contrato-de-medicao.md, que por sua vez os
# tira do briefing. O unico que NAO esta no briefing e o de teleporte da viga
# (0.5 u): e uma escolha minha, justificada la, e fica aqui em um so lugar para
# poder ser trocada sem tocar em nenhuma stack.
LIMIARES = {
    "fps_min": 60.0,
    "frame_p99_ms_max": 16.67,
    "fracao_amostras_ok": 0.99,
    "carry_jump_u_max": 0.5,
    "input_ms_p99_max": 100.0,
    "drift_u_max": 0.15,
    "drift_apos_s": 300.0,
    "duracao_minima_s": 600.0,
    "instancias": 4,
}


class Resultado:
    def __init__(self, nome, status, detalhe):
        self.nome = nome
        self.status = status  # PASS | FAIL | INFO
        self.detalhe = detalhe


def pares(resto):
    """Quebra 'a=1 b=2' em dict. Valores nao contem espaco, por contrato."""
    saida = {}
    for tok in resto.split():
        if "=" in tok:
            chave, _, valor = tok.partition("=")
            saida[chave] = valor
    return saida


def num(dic, chave, padrao=None):
    """Le um campo numerico. Devolve padrao se ausente ou ilegivel.

    Nunca levanta: campo ausente tem que virar FAIL legivel, nao stack trace.
    """
    bruto = dic.get(chave)
    if bruto is None:
        return padrao
    try:
        return float(bruto)
    except ValueError:
        return padrao


def percentil(valores, q):
    """Percentil por posto mais proximo. Sem numpy, sem interpolacao."""
    if not valores:
        return None
    ordenado = sorted(valores)
    k = max(0, min(len(ordenado) - 1, int(math.ceil(q * len(ordenado))) - 1))
    return ordenado[k]


class Corrida:
    def __init__(self):
        self.metas = []
        self.amostras = []
        self.eventos = []
        self.posicoes = []
        self.arquivos = []
        self.linhas_lidas = 0
        self.linhas_ignoradas = 0


def carregar(diretorio):
    corrida = Corrida()
    caminhos = sorted(
        os.path.join(diretorio, n)
        for n in os.listdir(diretorio)
        if n.endswith(".log") or n.endswith(".txt")
    )
    for caminho in caminhos:
        corrida.arquivos.append(caminho)
        with open(caminho, "r", encoding="utf-8", errors="replace") as fh:
            for linha in fh:
                linha = linha.strip()
                if not linha:
                    continue
                corrida.linhas_lidas += 1
                if linha.startswith(TAG_META + " "):
                    corrida.metas.append(pares(linha[len(TAG_META):]))
                elif linha.startswith(TAG_POS + " "):
                    corrida.posicoes.append(pares(linha[len(TAG_POS):]))
                elif linha.startswith(TAG_EVENT + " "):
                    corrida.eventos.append(pares(linha[len(TAG_EVENT):]))
                elif linha.startswith(TAG_SAMPLE + " "):
                    corrida.amostras.append(pares(linha[len(TAG_SAMPLE):]))
                else:
                    corrida.linhas_ignoradas += 1
    return corrida


def checar_integridade(corrida, instancias_esperadas):
    """A guarda contra o falso verde.

    Um log vazio, um caminho errado ou um arquivo truncado produzem "zero
    excecoes" e "zero drift" — que sao os melhores numeros possiveis pelo
    motivo errado. Nada e avaliado antes daqui passar.
    """
    problemas = []
    if not corrida.arquivos:
        problemas.append("nenhum arquivo .log/.txt no diretorio")
    if not corrida.metas:
        problemas.append("nenhuma linha %s - os logs nao seguem o contrato" % TAG_META)
    if len(corrida.metas) != len(corrida.arquivos):
        problemas.append(
            "%d arquivo(s) mas %d cabecalho(s) %s - um por instancia"
            % (len(corrida.arquivos), len(corrida.metas), TAG_META)
        )
    if len(corrida.metas) != instancias_esperadas:
        problemas.append(
            "%d instancia(s), esperado %d"
            % (len(corrida.metas), instancias_esperadas)
        )
    runs = {m.get("run") for m in corrida.metas}
    if len(runs) > 1:
        problemas.append("logs de corridas diferentes misturados: %s" % sorted(runs))
    hosts = [m for m in corrida.metas if m.get("role") == "host"]
    if len(hosts) != 1:
        problemas.append("%d host(s), esperado exatamente 1" % len(hosts))
    if not corrida.amostras:
        problemas.append("zero linhas %s - nada foi medido" % TAG_SAMPLE)

    if problemas:
        return Resultado("integridade", "FAIL", "; ".join(problemas))
    detalhe = (
        "%d arquivos, %d amostras, %d eventos, %d posicoes, %d linha(s) fora do contrato"
        % (
            len(corrida.arquivos),
            len(corrida.amostras),
            len(corrida.eventos),
            len(corrida.posicoes),
            corrida.linhas_ignoradas,
        )
    )
    return Resultado("integridade", "PASS", detalhe)


def checar_procedencia(corrida):
    """Diz de onde o numero veio. Nunca reprova; impede o relatorio de mentir."""
    transportes = sorted({m.get("transport", "?") for m in corrida.metas})
    rtts = sorted({m.get("rtt_ms", "?") for m in corrida.metas})
    perdas = sorted({m.get("loss_pct", "?") for m in corrida.metas})
    builds = sorted({m.get("build", "?") for m in corrida.metas})
    flavors = sorted({m.get("build_flavor", "?") for m in corrida.metas})
    aviso = ""
    if transportes == ["local"]:
        aviso = "  <<< transporte LOCAL: estes numeros NAO valem como 'sobre o relay da Steam'"
    if len(builds) > 1:
        aviso += "  <<< builds diferentes entre instancias: %s" % builds
    if len(flavors) > 1:
        # Uma ponta com hooks de profiler e sem stripping e a outra nao: qualquer
        # numero de tempo desta corrida descreve duas maquinas diferentes.
        aviso += "  <<< FLAVORS diferentes entre instancias: %s" % flavors
    if flavors == ["development"]:
        # Nao reprova: e a unica forma de injetar RTT (docs/99 20). Mas o fps daqui
        # nao e o fps do produto, e isso tem que estar escrito ao lado do numero.
        aviso += ("  <<< DEVELOPMENT build: sem stripping e com hooks de profiler. "
                  "fps daqui NAO vale como fps de release")
    return Resultado(
        "procedencia",
        "INFO",
        "transporte=%s rtt_injetado=%sms perda_injetada=%s%% build=%s flavor=%s%s"
        % (",".join(transportes), ",".join(rtts), ",".join(perdas), ",".join(builds),
           ",".join(flavors), aviso),
    )


def checar_duracao(corrida, minimo):
    """Quanto a corrida COBRE, que nao e o intervalo entre a 1a e a ultima amostra.

    Corrigido em 17/09. A versao anterior usava max(t)-min(t) das amostras. Uma
    corrida de 600 s exatos dava 599.1 s e reprovava, porque cada amostra cobre o
    intervalo ate a proxima e o span perde a ultima. O erro e sempre para menos.

    ATENCAO ao ler este diff: e uma checagem que ANTES reprovava e agora aprova, e
    esse e justamente o movimento que merece desconfianca. A primeira tentativa de
    conserto era pior e foi descartada: usar max(t) de QUALQUER linha fazia a
    corrida congelada de 17/09 (errors/05) PASSAR com 1632 s de "cobertura", porque
    o t do shutdown e relogio de parede e seguiu correndo com o processo parado.
    Por isso a conta usa so amostras -- linha de amostra so existe se algo foi
    medido -- e o passo sai da mediana da propria corrida, nao de uma constante.
    """
    tempos = sorted(t for t in (num(a, "t") for a in corrida.amostras) if t is not None)
    if not tempos:
        return Resultado("duracao", "FAIL", "nenhuma amostra tem campo t=")

    span = tempos[-1] - tempos[0]

    # Cada amostra COBRE o intervalo ate a proxima, entao o span perde uma. O
    # intervalo sai da mediana da propria corrida em vez de uma constante, pelo
    # mesmo motivo de relogio_coerente: serve para qualquer cadencia de emissao.
    intervalos = sorted(tempos[i] - tempos[i - 1] for i in range(1, len(tempos)))
    passo = intervalos[len(intervalos) // 2] if intervalos else 0.0
    cobertura = span + passo

    status = "PASS" if cobertura >= minimo else "FAIL"
    return Resultado(
        "duracao", status,
        "cobertura %.1f s (minimo %.0f s) = span %.1f + 1 intervalo de %.2f s"
        % (cobertura, minimo, span, passo),
    )


def checar_coerencia_do_relogio(corrida):
    """O relogio de parede e o contador de passos tem que andar juntos.

    Origem: a corrida de 600s de 17/09. O player do Unity para o loop quando a
    janela perde o foco, e ninguem clica na janela de um soak automatizado. A
    corrida congelou aos 96 s: `t` seguiu ate 1632 s porque e relogio de parede,
    `tick` parou em 4458. As amostras que sobraram eram todas boas -- fps alto,
    zero excecao, viga comportada -- porque medir menos e a forma mais facil de
    parecer bem.

    A duracao pegou aquele caso por sorte (o log ficou curto). Nao pega o caso
    geral: um congelamento no MEIO de uma corrida longa deixa duracao e contagem
    de amostras intactas. O sintoma direto e este -- t anda e tick nao.

    Nao usa dt do motor de proposito: a taxa esperada sai da mediana da propria
    corrida, entao a checagem serve igual para as tres candidatas, com qualquer
    passo de fisica.
    """
    problemas = []
    detalhes = []

    for papel_id in sorted({(a.get("role"), a.get("id")) for a in corrida.amostras}):
        amostras = [a for a in corrida.amostras
                    if (a.get("role"), a.get("id")) == papel_id]
        serie = []
        for a in amostras:
            t, tick = num(a, "t"), num(a, "tick")
            if t is not None and tick is not None:
                serie.append((t, tick))

        serie.sort()
        if len(serie) < 3:
            continue

        taxas = []
        for i in range(1, len(serie)):
            dt = serie[i][0] - serie[i - 1][0]
            dtick = serie[i][1] - serie[i - 1][1]
            if dt > 0:
                taxas.append((serie[i][0], dtick / dt))

        if not taxas:
            continue

        mediana = sorted(v for _, v in taxas)[len(taxas) // 2]

        # Mediana zero significa que o tick ficou parado na MAIOR PARTE da corrida.
        # A versao anterior fazia `continue` aqui e desistia da checagem em
        # silencio -- justo no caso pior, o congelamento que domina a corrida. Um
        # log sintetico com 6 de 10 intervalos travados passou limpo por causa
        # disso, e foi assim que o buraco apareceu.
        if mediana <= 0:
            problemas.append(
                "%s/%s: tick parado na maior parte da corrida (taxa mediana 0 em "
                "%d intervalos)" % (papel_id[0], papel_id[1], len(taxas))
            )
            continue

        travadas = [(t, v) for t, v in taxas if v < mediana * 0.5]
        detalhes.append("%s/%s: %.1f tick/s mediano" % (papel_id[0], papel_id[1], mediana))

        if travadas:
            pior = min(travadas, key=lambda p: p[1])
            problemas.append(
                "%s/%s: %d intervalo(s) abaixo de metade da taxa; pior em t=%.3f com "
                "%.1f tick/s contra %.1f mediano"
                % (papel_id[0], papel_id[1], len(travadas), pior[0], pior[1], mediana)
            )

    if problemas:
        return Resultado("relogio_coerente", "FAIL", "; ".join(problemas))
    if not detalhes:
        return Resultado("relogio_coerente", "FAIL", "nenhuma amostra com t= e tick=")
    return Resultado("relogio_coerente", "PASS", "; ".join(detalhes))


def checar_fps_host(corrida):
    host = [a for a in corrida.amostras if a.get("role") == "host"]
    if not host:
        return Resultado("fps_host", "FAIL", "nenhuma amostra com role=host")
    faltando = [a for a in host if num(a, "fps") is None or num(a, "frame_p99_ms") is None]
    if faltando:
        return Resultado(
            "fps_host", "FAIL",
            "%d de %d amostras do host sem fps= ou frame_p99_ms=" % (len(faltando), len(host))
        )
    ok = [
        a for a in host
        if num(a, "fps") >= LIMIARES["fps_min"]
        and num(a, "frame_p99_ms") <= LIMIARES["frame_p99_ms_max"]
    ]
    fracao = len(ok) / len(host)
    status = "PASS" if fracao >= LIMIARES["fracao_amostras_ok"] else "FAIL"
    piores = sorted(host, key=lambda a: num(a, "fps"))[:3]
    amostra_ruim = ", ".join(
        "t=%s fps=%s p99=%sms" % (a.get("t"), a.get("fps"), a.get("frame_p99_ms"))
        for a in piores
    )
    return Resultado(
        "fps_host", status,
        "%.2f%% das %d amostras com fps>=%.0f e p99<=%.2fms (exigido %.0f%%) | piores: %s"
        % (fracao * 100, len(host), LIMIARES["fps_min"], LIMIARES["frame_p99_ms_max"],
           LIMIARES["fracao_amostras_ok"] * 100, amostra_ruim)
    )


def checar_banda(corrida):
    """O briefing manda REPORTAR media e pico. Nao da teto — entao nao reprovo.

    Inventar um teto seria eu decidindo o que e aceitavel, e depois otimizar
    contra ele. As duas coisas sao proibidas pelo briefing.
    """
    clientes = [a for a in corrida.amostras if a.get("role") == "client"]
    if not clientes:
        return Resultado("banda_por_cliente", "INFO", "nenhuma amostra de cliente")
    por_id = {}
    for a in clientes:
        por_id.setdefault(a.get("id", "?"), []).append(a)
    partes = []
    for ident in sorted(por_id):
        rx = [num(a, "rx_KBps") for a in por_id[ident]]
        tx = [num(a, "tx_KBps") for a in por_id[ident]]
        # -1 e "NAO INSTRUMENTADO", nao "zero bytes". Media-lo junto produziria um
        # numero negativo que parece dado. A candidata B cai aqui: o FishNet 4.7.3
        # nao expoe contagem de bytes de socket fora de build de desenvolvimento, e
        # as classes que a guardam sao internal ao assembly dele.
        rx = [v for v in rx if v is not None and v >= 0]
        tx = [v for v in tx if v is not None and v >= 0]
        if not rx or not tx:
            partes.append("id=%s BANDA NAO INSTRUMENTADA (rx/tx vieram -1 ou ausentes)"
                          % ident)
            continue
        partes.append(
            "id=%s rx %.1f/%.1f KBps (med/pico) tx %.1f/%.1f"
            % (ident, sum(rx) / len(rx), max(rx), sum(tx) / len(tx), max(tx))
        )
    return Resultado("banda_por_cliente", "INFO", " | ".join(partes))


def checar_teleporte(corrida):
    com_campo = [a for a in corrida.amostras if num(a, "carry_jump_u") is not None]
    if not com_campo:
        return Resultado("viga_nao_teleporta", "FAIL", "nenhuma amostra tem carry_jump_u=")
    limite = LIMIARES["carry_jump_u_max"]
    ruins = [a for a in com_campo if num(a, "carry_jump_u") > limite]
    pior = max(com_campo, key=lambda a: num(a, "carry_jump_u"))
    status = "PASS" if not ruins else "FAIL"
    return Resultado(
        "viga_nao_teleporta", status,
        "maior salto %.3f u (limite %.2f) em t=%s id=%s | %d amostra(s) acima"
        % (num(pior, "carry_jump_u"), limite, pior.get("t"), pior.get("id"), len(ruins))
    )


def checar_input(corrida):
    """-1 e NAO INSTRUMENTADO, e nao "respondeu em -1 ms".

    Bug de falso verde achado em 17/09: o cliente emitia input_ms_p99=-1 por nao
    ter o campo instrumentado, e esta checagem devolvia PASS com "pior p99 -1.0 ms
    (limite 100)". Ou seja, o campo NAO medido passava com folga -- o juiz dando
    verde justamente para quem nao foi medido, que e o contrario do que ele existe
    para fazer. A convencao -1 e do proprio contrato (o host usa -1 em drift), e
    toda checagem que le um campo assim precisa filtrar antes de comparar.
    """
    clientes = [a for a in corrida.amostras
                if a.get("role") == "client"
                and num(a, "input_ms_p99") is not None
                and num(a, "input_ms_p99") >= 0]
    if not clientes:
        return Resultado("resposta_do_input", "FAIL",
                         "nenhuma amostra de cliente com input_ms_p99 medido "
                         "(ausente, ou -1 = nao instrumentado)")
    limite = LIMIARES["input_ms_p99_max"]
    ruins = [a for a in clientes if num(a, "input_ms_p99") > limite]
    pior = max(clientes, key=lambda a: num(a, "input_ms_p99"))
    status = "PASS" if not ruins else "FAIL"
    return Resultado(
        "resposta_do_input", status,
        "pior p99 %.1f ms (limite %.0f) em t=%s id=%s | %d amostra(s) acima"
        % (num(pior, "input_ms_p99"), limite, pior.get("t"), pior.get("id"), len(ruins))
    )


def serie_de_posicao(corrida, papel, identidade=None):
    """Posicoes de uma instancia, por CORPO e por tick de rede.

    Devolve {obj: {ntick: (x, y, z)}}. O `obj` e o ObjectId do FishNet, que e o unico
    identificador que vale nas duas pontas: nome nao serve (o cliente recebe clones do
    prefab, e nome nao e sincronizado) e posicao em lista tambem nao (a ordem de
    chegada no cliente nao e a de criacao no host).

    Linha com campo faltando ou ilegivel e descartada em silencio de proposito: o
    contrato ja obriga o emissor, e uma linha torta nao pode derrubar a avaliacao
    inteira. Quem acusa emissor quebrado e checar_integridade, contando linhas.
    """
    series = {}
    for p in corrida.posicoes:
        if p.get("role") != papel:
            continue
        if identidade is not None and p.get("id") != identidade:
            continue
        ntick = num(p, "ntick")
        x, y, z = num(p, "x"), num(p, "y"), num(p, "z")
        if None in (ntick, x, y, z):
            continue
        # `obj` ausente = formato anterior a changes/19, com um corpo so. Cai num
        # balde unico em vez de ser descartado: log antigo continua avaliavel.
        obj = p.get("obj", "_unico")
        series.setdefault(obj, {})[int(ntick)] = (x, y, z)
    return series


def ticks_de(series):
    """Todos os ntick presentes em {obj: {ntick: pos}}, sem repetir."""
    vistos = set()
    for por_tick in series.values():
        vistos.update(por_tick)
    return vistos


def tempo_do_host_por_ntick(corrida):
    """{ntick: t do host}. E o unico relogio que diz ha quanto tempo o MUNDO simula.

    O `t` do cliente nao serve para o corte dos 5 min do briefing: um cliente que
    entrou atrasado esta aos 60 s do proprio relogio olhando um mundo de 6 min.
    """
    tempos = {}
    for p in corrida.posicoes:
        if p.get("role") != "host":
            continue
        ntick, t = num(p, "ntick"), num(p, "t")
        if ntick is None or t is None:
            continue
        tempos[int(ntick)] = t
    return tempos


def distancia(a, b):
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def distancias_com_deslocamento(host, cliente, deslocamento):
    """Distancias host x cliente, por corpo, com a serie do cliente deslocada N ticks.

    `deslocamento` positivo significa que o cliente esta ATRASADO: o que ele mostra no
    tick T e comparado com o que o host tinha em T - deslocamento.

    Devolve [(distancia, obj, ntick)], para o pior caso poder dizer QUAL corpo e
    QUANDO — "0.68 u" sem isso nao diz se e a viga ou uma caixa que caiu da pilha.

    O deslocamento e o MESMO para todos os corpos de propósito: atraso e propriedade
    da conexao, nao de cada objeto. Buscar um deslocamento por corpo deixaria cada um
    escolher o que mais o favorece, e o numero resultante nao descreveria nada.
    """
    saida = []
    for obj, por_tick_cliente in cliente.items():
        por_tick_host = host.get(obj)
        if not por_tick_host:
            continue
        for ntick, pos_cliente in por_tick_cliente.items():
            pos_host = por_tick_host.get(ntick - deslocamento)
            if pos_host is not None:
                saida.append((distancia(pos_host, pos_cliente), obj, ntick))
    return saida


def maior_distancia_com_deslocamento(host, cliente, deslocamento):
    """Idem, so o pior caso. Devolve (pior, quantos_pares)."""
    ds = distancias_com_deslocamento(host, cliente, deslocamento)
    if not ds:
        return None, 0
    return max(ds), len(ds)


def entre(a, b, t):
    """Ponto entre a e b. t=0 devolve a, t=1 devolve b."""
    return (a[0] + (b[0] - a[0]) * t,
            a[1] + (b[1] - a[1]) * t,
            a[2] + (b[2] - a[2]) * t)


def maior_distancia_sub_tick(host, cliente, inteiro, fracao):
    """Pior distancia com atraso FRACIONARIO de `inteiro + fracao` ticks.

    Existe porque o atraso real nao cai em tick redondo, e alinhar so por tick
    inteiro deixa um residuo que PARECE divergencia. Medido em 18/09: com RTT zero o
    `drift_alinhado` dava 0.0647 u, que a 0.108 u/tick e exatamente 0.60 tick — a
    parte fracionaria de um atraso de ~4.6 ticks, e nao estado errado.

    A posicao do host no instante fracionario (T - inteiro - fracao) sai interpolando
    entre os dois ticks vizinhos. Interpolar a serie do HOST e legitimo aqui: ela e a
    verdade amostrada, e o que se procura e onde ela estava ENTRE duas amostras.
    """
    pior = None
    usados = 0
    for obj, por_tick_cliente in cliente.items():
        por_tick_host = host.get(obj)
        if not por_tick_host:
            continue
        for ntick, pos_cliente in por_tick_cliente.items():
            depois = por_tick_host.get(ntick - inteiro)
            antes = por_tick_host.get(ntick - inteiro - 1)
            if depois is None or antes is None:
                continue
            d = distancia(entre(antes, depois, 1.0 - fracao), pos_cliente)
            usados += 1
            if pior is None or d > pior:
                pior = d
    return pior, usados


def checar_drift(corrida):
    """Drift e comparacao ENTRE instancias, e por isso nao sai da linha de amostra.

    Ver docs/00, secao "Drift nao cabe na linha de amostra": no instante em que o
    cliente emite, ele nao conhece a posicao autoritativa do host. Quem cruza e este
    avaliador, que tem os dois logs, pela serie [SOAK-POS] indexada no tick da rede.

    Dois numeros, nao um:
      - `drift_mesmo_ntick` e o erro que uma pessoa veria na tela. Inclui o atraso do
        buffer de interpolacao. E nele que o limiar de 0.15 u do briefing e aplicado;
      - `drift_alinhado` + `atraso_ticks` separam atraso de divergencia. Um cliente 3
        ticks atras mas correto nao e a mesma falha que um cliente no tick certo e no
        lugar errado, e um FAIL sem essa separacao nao da para explicar.
    """
    limite = LIMIARES["drift_u_max"]
    apos = LIMIARES["drift_apos_s"]

    host_completo = serie_de_posicao(corrida, "host")
    tempos = tempo_do_host_por_ntick(corrida)

    # O corte do briefing: "drift ... apos 5 min de simulacao continua". Vale o
    # relogio do HOST, e nenhum tick sem tempo conhecido entra — sem o t nao da para
    # afirmar que ele esta depois do corte, e afirmar seria inventar cobertura.
    host = {}
    for obj, por_tick in host_completo.items():
        depois = {
            ntick: pos for ntick, pos in por_tick.items()
            if tempos.get(ntick) is not None and tempos[ntick] >= apos
        }
        if depois:
            host[obj] = depois

    if host_completo and not host:
        return Resultado(
            "drift", "FAIL",
            "a corrida nao chegou aos %.0f s que o briefing exige: o host tem %d tick(s) "
            "de posicao, nenhum depois do corte. Drift so significa algo depois de o "
            "mundo simular continuamente." % (apos, len(ticks_de(host_completo)))
        )

    if not host:
        return Resultado(
            "drift", "FAIL",
            "nenhuma linha %s de role=host - sem a serie do host nao existe verdade "
            "contra a qual comparar" % TAG_POS
        )

    clientes = sorted({
        p.get("id") for p in corrida.posicoes if p.get("role") == "client"
    })
    if not clientes:
        return Resultado(
            "drift", "FAIL",
            "nenhuma linha %s de role=client - a corrida nao teve cliente, ou o "
            "emissor nao instrumentou posicao" % TAG_POS
        )

    piores = []
    for identidade in clientes:
        cliente = serie_de_posicao(corrida, "client", identidade)
        no_tick = distancias_com_deslocamento(host, cliente, 0)
        if not no_tick:
            piores.append({"id": identidade, "mesmo": None})
            continue

        distancias = [d for d, _, _ in no_tick]
        pior_par = max(no_tick)

        # Passo 1: melhor tick INTEIRO. Varre a faixa toda.
        melhor = None
        melhor_deslocamento = 0
        for deslocamento in range(0, BUSCA_ATRASO_TICKS + 1):
            valor, usados = maior_distancia_com_deslocamento(host, cliente, deslocamento)
            if valor is None or usados == 0:
                continue
            if melhor is None or valor[0] < melhor:
                melhor = valor[0]
                melhor_deslocamento = deslocamento

        # Passo 2: refina em SUB-TICK, so na vizinhanca do melhor inteiro. Varrer a
        # faixa inteira em passos de 0.1 custaria 10x e nao acharia nada novo: a
        # funcao tem um minimo so, e o passo 1 ja disse onde ele esta.
        melhor_atraso = float(melhor_deslocamento)
        for inteiro in (melhor_deslocamento - 1, melhor_deslocamento, melhor_deslocamento + 1):
            if inteiro < 0:
                continue
            for decimo in range(0, 10):
                fracao = decimo / 10.0
                valor, usados = maior_distancia_sub_tick(host, cliente, inteiro, fracao)
                if valor is None or usados == 0:
                    continue
                if valor < melhor:
                    melhor = valor
                    melhor_atraso = inteiro + fracao

        piores.append({
            "id": identidade,
            "mesmo": pior_par[0],
            "obj": pior_par[1],
            "ntick": pior_par[2],
            "alinhado": melhor,
            "atraso": melhor_atraso,
            "pares": len(no_tick),
            "corpos": len(set(cliente) & set(host)),
            "p99": percentil(distancias, 0.99),
        })

    sem_par = [p for p in piores if p["mesmo"] is None]
    if sem_par:
        return Resultado(
            "drift", "FAIL",
            "cliente(s) %s sem nenhum par (corpo, ntick) em comum com o host - as "
            "series existem mas nao se cruzam, entao nada foi comparado"
            % ", ".join(str(p["id"]) for p in sem_par)
        )

    acima = [p for p in piores if p["mesmo"] >= limite]
    pior = max(piores, key=lambda p: p["mesmo"])

    aviso = ""
    if pior["atraso"] >= BUSCA_ATRASO_TICKS:
        aviso = (" | ATENCAO: o alinhamento encostou no teto de busca (%d ticks), "
                 "entao esse 'atraso' nao e atraso - a serie do cliente nao casa com "
                 "a do host em nenhum deslocamento procurado"
                 % BUSCA_ATRASO_TICKS)

    status = "PASS" if not acima else "FAIL"
    return Resultado(
        "drift", status,
        "pior id=%s: drift_mesmo_ntick max %.4f u (obj=%s ntick=%s) p99 %.4f u "
        "(limite %.2f) | alinhado %.4f u com atraso de %.1f tick(s) | %d corpo(s) e "
        "%d par(es) comparados, %d cliente(s) acima do limite%s"
        % (pior["id"], pior["mesmo"], pior["obj"], pior["ntick"], pior["p99"], limite,
           pior["alinhado"], pior["atraso"], pior["corpos"], pior["pares"],
           len(acima), aviso)
    )


def checar_late_join(corrida):
    """Late join pergunta 'recebeu TUDO?'. Quem pergunta 'esta no lugar certo?' e o drift.

    A versao anterior comparava o `world_hash` do cliente com o de uma amostra do host
    **no mesmo tick**, e era insalubre por dois motivos independentes:

    1. casava pelo `tick` LOCAL, que nao e comparavel entre processos — cada um comeca
       do zero quando sobe. Reprovava com "nenhuma amostra do host no tick=5" mesmo
       quando tudo estava certo;
    2. mesmo com a chave certa, **o hash nunca bateria**. O cliente renderiza
       interpolado, alguns ticks atras do host (medido: 5 ticks). O hash quantiza a
       0.01 u e a viga anda ~0.108 u por tick — um unico tick de diferenca ja muda o
       hash. Exigir igualdade num instante e exigir latencia zero.

    Ou seja: era uma checagem que reprovava replicacao correta e nao tinha como passar.
    Agora ela pergunta o que de fato e late join — **completude**: o cliente recebeu o
    mesmo CONJUNTO de corpos que o host criou? Isso e imune a atraso. Se o conjunto bate
    mas as posicoes estao erradas, quem acusa e `drift`, que ja existe e ja separa
    atraso de divergencia.
    """
    feitos = [e for e in corrida.eventos if e.get("ev") == "late_join_done"]
    se_perdeu = [e for e in corrida.eventos if e.get("ev") == "late_join_timeout"]
    if not feitos:
        if se_perdeu:
            # Muito melhor que "nenhum evento": "chegaram 149 de 151" e "nao chegou
            # nada" mandam procurar em lugares diferentes.
            return Resultado(
                "late_join", "FAIL",
                "; ".join(
                    "id=%s desistiu apos %.1f ms com %s de %s corpo(s)"
                    % (e.get("id"), num(e, "esperou_ms", -1.0),
                       e.get("bodies", "?"), e.get("esperados", "?"))
                    for e in se_perdeu
                )
            )
        return Resultado("late_join", "FAIL", "nenhum evento ev=late_join_done")

    criados = [
        e for e in corrida.eventos
        if e.get("ev") == "spawn_done" and e.get("role") == "host"
    ]
    if not criados:
        return Resultado(
            "late_join", "FAIL",
            "nenhum ev=spawn_done do host - sem saber quantos corpos o mundo tem, "
            "'recebeu o estado completo' nao tem contra o que ser conferido"
        )
    esperados = num(criados[0], "bodies")
    if esperados is None:
        return Resultado("late_join", "FAIL", "ev=spawn_done do host sem campo bodies=")

    problemas = []
    for ev in feitos:
        ident = ev.get("id")
        recebidos = num(ev, "bodies")
        if recebidos is None:
            problemas.append("id=%s: ev=late_join_done sem campo bodies=" % ident)
            continue
        if recebidos != esperados:
            problemas.append(
                "id=%s recebeu %d de %d corpo(s) - estado INCOMPLETO"
                % (ident, int(recebidos), int(esperados))
            )
        if num(ev, "elapsed_ms") is None:
            problemas.append("id=%s: ev=late_join_done sem elapsed_ms=" % ident)

    status = "PASS" if not problemas else "FAIL"
    if not problemas:
        detalhe = (
            "%d late join(s), %d de %d corpo(s) em cada; pior elapsed %.1f ms"
            % (len(feitos), int(esperados), int(esperados),
               max(num(e, "elapsed_ms", 0.0) for e in feitos))
        )
    else:
        detalhe = "; ".join(problemas)
    return Resultado("late_join", status, detalhe)


def checar_queda_do_host(corrida):
    saiu = [e for e in corrida.eventos if e.get("ev") == "host_quit"]
    if not saiu:
        return Resultado("queda_do_host", "FAIL", "nenhum evento ev=host_quit na corrida")
    ids_clientes = {m.get("id") for m in corrida.metas if m.get("role") == "client"}
    limpos, sujos, calados = set(), [], []
    for ident in sorted(ids_clientes):
        desligou = [
            e for e in corrida.eventos
            if e.get("ev") == "shutdown" and e.get("id") == ident
        ]
        if not desligou:
            calados.append(ident)
        elif all(e.get("clean") == "1" for e in desligou):
            limpos.add(ident)
        else:
            sujos.append("%s(%s)" % (ident, desligou[0].get("reason", "?")))
    problemas = []
    if calados:
        problemas.append("sem ev=shutdown: id %s" % ", ".join(calados))
    if sujos:
        problemas.append("shutdown clean=0: %s" % ", ".join(sujos))
    status = "PASS" if not problemas else "FAIL"
    detalhe = ("%d de %d cliente(s) encerraram com clean=1" % (len(limpos), len(ids_clientes))
               if not problemas else "; ".join(problemas))
    return Resultado("queda_do_host", status, detalhe)


def checar_excecoes(corrida):
    excecoes = [e for e in corrida.eventos if e.get("ev") == "exception"]
    if not excecoes:
        return Resultado("zero_excecoes", "PASS", "nenhuma linha ev=exception")
    onde = "; ".join(
        "t=%s id=%s %s" % (e.get("t"), e.get("id"), e.get("where", "?"))
        for e in excecoes[:5]
    )
    return Resultado("zero_excecoes", "FAIL", "%d excecao(oes): %s" % (len(excecoes), onde))


def avaliar(diretorio, instancias, duracao_minima):
    try:
        corrida = carregar(diretorio)
    except OSError as erro:
        print("ERRO: nao consegui ler %s: %s" % (diretorio, erro))
        return 2, []

    integridade = checar_integridade(corrida, instancias)
    if integridade.status == "FAIL":
        # Nada mais e avaliado: metrica calculada sobre log quebrado da numero
        # bonito pelo motivo errado, e numero bonito nao dispara alarme nenhum.
        return 1, [integridade]

    resultados = [
        integridade,
        checar_procedencia(corrida),
        checar_duracao(corrida, duracao_minima),
        checar_coerencia_do_relogio(corrida),
        checar_fps_host(corrida),
        checar_banda(corrida),
        checar_teleporte(corrida),
        checar_input(corrida),
        checar_drift(corrida),
        checar_late_join(corrida),
        checar_queda_do_host(corrida),
        checar_excecoes(corrida),
    ]
    saida = 1 if any(r.status == "FAIL" for r in resultados) else 0
    return saida, resultados


def imprimir(resultados, diretorio):
    largura = max(len(r.nome) for r in resultados)
    print("SOAK - %s" % os.path.abspath(diretorio))
    print("-" * 78)
    for r in resultados:
        print("%-4s  %-*s  %s" % (r.status, largura, r.nome, r.detalhe))
    print("-" * 78)
    reprovadas = [r.nome for r in resultados if r.status == "FAIL"]
    if reprovadas:
        print("RESULTADO: FAIL - %s" % ", ".join(reprovadas))
    else:
        print("RESULTADO: PASS")


def main(argv):
    ap = argparse.ArgumentParser(description="Avalia uma corrida de soak.")
    ap.add_argument("diretorio", help="pasta com os .log das instancias")
    ap.add_argument("--instancias", type=int, default=LIMIARES["instancias"],
                    help="quantas instancias a corrida deve ter (padrao 4)")
    ap.add_argument("--duracao-minima", type=float, default=LIMIARES["duracao_minima_s"],
                    help="segundos minimos de corrida (padrao 600)")
    args = ap.parse_args(argv)

    codigo, resultados = avaliar(args.diretorio, args.instancias, args.duracao_minima)
    if resultados:
        imprimir(resultados, args.diretorio)
    return codigo


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
