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
    detalhe = "%d arquivos, %d amostras, %d eventos, %d linha(s) fora do contrato" % (
        len(corrida.arquivos),
        len(corrida.amostras),
        len(corrida.eventos),
        corrida.linhas_ignoradas,
    )
    return Resultado("integridade", "PASS", detalhe)


def checar_procedencia(corrida):
    """Diz de onde o numero veio. Nunca reprova; impede o relatorio de mentir."""
    transportes = sorted({m.get("transport", "?") for m in corrida.metas})
    rtts = sorted({m.get("rtt_ms", "?") for m in corrida.metas})
    perdas = sorted({m.get("loss_pct", "?") for m in corrida.metas})
    builds = sorted({m.get("build", "?") for m in corrida.metas})
    aviso = ""
    if transportes == ["local"]:
        aviso = "  <<< transporte LOCAL: estes numeros NAO valem como 'sobre o relay da Steam'"
    if len(builds) > 1:
        aviso += "  <<< builds diferentes entre instancias: %s" % builds
    return Resultado(
        "procedencia",
        "INFO",
        "transporte=%s rtt_injetado=%sms perda_injetada=%s%% build=%s%s"
        % (",".join(transportes), ",".join(rtts), ",".join(perdas), ",".join(builds), aviso),
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
        rx = [v for v in rx if v is not None]
        tx = [v for v in tx if v is not None]
        if not rx or not tx:
            partes.append("id=%s SEM CAMPO DE BANDA" % ident)
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
    clientes = [a for a in corrida.amostras
                if a.get("role") == "client" and num(a, "input_ms_p99") is not None]
    if not clientes:
        return Resultado("resposta_do_input", "FAIL",
                         "nenhuma amostra de cliente com input_ms_p99=")
    limite = LIMIARES["input_ms_p99_max"]
    ruins = [a for a in clientes if num(a, "input_ms_p99") > limite]
    pior = max(clientes, key=lambda a: num(a, "input_ms_p99"))
    status = "PASS" if not ruins else "FAIL"
    return Resultado(
        "resposta_do_input", status,
        "pior p99 %.1f ms (limite %.0f) em t=%s id=%s | %d amostra(s) acima"
        % (num(pior, "input_ms_p99"), limite, pior.get("t"), pior.get("id"), len(ruins))
    )


def checar_drift(corrida):
    apos = LIMIARES["drift_apos_s"]
    limite = LIMIARES["drift_u_max"]
    elegiveis = [
        a for a in corrida.amostras
        if a.get("role") == "client"
        and num(a, "t") is not None and num(a, "t") >= apos
        and num(a, "drift_max_u") is not None
    ]
    if not elegiveis:
        return Resultado(
            "drift", "FAIL",
            "nenhuma amostra de cliente com t>=%.0fs e drift_max_u= - a corrida "
            "nao chegou aos 5 min ou o campo nao foi emitido" % apos
        )
    ruins = [a for a in elegiveis if num(a, "drift_max_u") >= limite]
    pior = max(elegiveis, key=lambda a: num(a, "drift_max_u"))
    p99 = percentil([num(a, "drift_max_u") for a in elegiveis], 0.99)
    status = "PASS" if not ruins else "FAIL"
    return Resultado(
        "drift", status,
        "pior %.4f u, p99 %.4f u (limite %.2f) em t=%s id=%s | %d de %d amostra(s) acima"
        % (num(pior, "drift_max_u"), p99, limite, pior.get("t"), pior.get("id"),
           len(ruins), len(elegiveis))
    )


def checar_late_join(corrida):
    feitos = [e for e in corrida.eventos if e.get("ev") == "late_join_done"]
    if not feitos:
        return Resultado("late_join", "FAIL", "nenhum evento ev=late_join_done")
    problemas = []
    for ev in feitos:
        tick = ev.get("tick")
        hash_cliente = ev.get("world_hash")
        if hash_cliente is None:
            problemas.append("id=%s sem world_hash no evento" % ev.get("id"))
            continue
        do_host = [
            a for a in corrida.amostras
            if a.get("role") == "host" and a.get("tick") == tick
        ]
        if not do_host:
            problemas.append(
                "id=%s: nenhuma amostra do host no tick=%s para comparar" % (ev.get("id"), tick)
            )
            continue
        hash_host = do_host[0].get("world_hash")
        if hash_host != hash_cliente:
            problemas.append(
                "id=%s tick=%s: cliente %s != host %s"
                % (ev.get("id"), tick, hash_cliente, hash_host)
            )
    status = "PASS" if not problemas else "FAIL"
    detalhe = ("%d late join(s), estado conferido por world_hash contra o host" % len(feitos)
               if not problemas else "; ".join(problemas))
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
