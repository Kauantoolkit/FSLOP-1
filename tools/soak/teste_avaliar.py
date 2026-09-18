#!/usr/bin/env python3
"""
Teste do avaliador. Roda com:  python teste_avaliar.py

Existe por um motivo so: um avaliador que nunca foi visto REPROVANDO nao foi
verificado, foi apenas executado. Cada caso abaixo quebra uma coisa de
proposito e exige que o avaliador aponte exatamente aquela metrica — nem outra,
nem nenhuma.

O caso `base` e o inverso: tudo certo, e o avaliador nao pode reprovar nada.
Sem ele, um avaliador que reprovasse tudo tambem passaria em todos os outros.

Stdlib apenas.
"""

import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import avaliar as av  # noqa: E402

HASH = "a1b2c3d4"
CORPOS = 151      # 150 caixas + a viga, como o teste minimo do briefing manda
N_AMOSTRAS = 11
T0 = 296.0          # comeca antes dos 300 s para o corte de drift ser exercido
TICK0 = 17760
TICK_LATE = TICK0 + 2 * 60


def meta(role, ident, **over):
    campos = {
        "run": "r001", "stack": "B", "role": role, "id": str(ident), "pid": "1000",
        "build": "abc1234", "engine": "6000.6.0f1", "transport": "local",
        "rtt_ms": "150", "loss_pct": "3.0", "bodies": "150",
        "started": "2026-09-06T03:00:00Z",
    }
    campos.update(over)
    return "[SOAK-META] " + " ".join("%s=%s" % kv for kv in campos.items())


def amostra(role, ident, i, **over):
    campos = {
        "t": "%.3f" % (T0 + i), "tick": str(TICK0 + i * 60), "role": role, "id": str(ident),
        "fps": "62.0", "frame_p99_ms": "15.00",
        "rx_KBps": "18.5", "tx_KBps": "4.2",
        "drift_max_u": ("-1" if role == "host" else "0.0200"),
        "drift_p99_u": ("-1" if role == "host" else "0.0150"),
        "carry_jump_u": "0.020", "input_ms_p99": ("-1" if role == "host" else "40.0"),
        "bodies_awake": "12", "world_hash": HASH,
    }
    campos.update(over)
    return "[SOAK] " + " ".join("%s=%s" % kv for kv in campos.items())


def evento(role, ident, ev, tick, t, **extras):
    campos = {"t": "%.3f" % t, "tick": str(tick), "role": role, "id": str(ident), "ev": ev}
    campos.update(extras)
    return "[SOAK-EV] " + " ".join("%s=%s" % kv for kv in campos.items())


# Serie de posicao. A viga anda em linha reta no eixo x, 0.05 u por tick de rede
# (1.5 u/s a 30 Hz). Reta de proposito: com trajetoria periodica, um deslocamento
# errado poderia casar por coincidencia e o teste ficaria verde sem significar nada.
N_POS = 400
NTICK0 = TICK0
PASSO_U = 0.05
HZ_REDE = 30.0


def pos_do_host(i):
    """Posicao do host no i-esimo tick de rede da serie."""
    return (i * PASSO_U, 1.0, 0.0)


def posicao(role, ident, ntick, t, xyz, obj=None):
    campo_obj = "" if obj is None else (" obj=%s" % obj)
    return ("[SOAK-POS] ntick=%d t=%.3f role=%s id=%s%s x=%.4f y=%.4f z=%.4f"
            % (ntick, t, role, ident, campo_obj, xyz[0], xyz[1], xyz[2]))


def serie_pos(role, ident, desloca_ticks=0, offset_y=0.0, so_antes_de=None,
              offset_y_antes=0.0):
    """Linhas [SOAK-POS] de uma instancia.

    desloca_ticks: o que ela mostra no tick N e o que o host tinha em N - desloca.
    offset_y:      erro constante perpendicular ao movimento. Perpendicular de
                   proposito — assim a busca de alinhamento NAO consegue escondê-lo,
                   que e exatamente o que se quer testar.
    so_antes_de:   se dado, `offset_y_antes` vale para t < esse valor e nada depois.
    """
    linhas = []
    for i in range(N_POS):
        ntick = NTICK0 + i
        t = T0 + i / HZ_REDE
        fonte = i - desloca_ticks
        if fonte < 0:
            continue
        x, y, z = pos_do_host(fonte)
        erro = offset_y
        if so_antes_de is not None:
            erro = offset_y_antes if t < so_antes_de else 0.0
        linhas.append(posicao(role, ident, ntick, t, (x, y + erro, z)))
    return linhas


def montar_base():
    """Devolve {nome_do_arquivo: [linhas]} de uma corrida que deve passar."""
    host = [meta("host", 0)]
    cliente = [meta("client", 1)]
    for i in range(N_AMOSTRAS):
        host.append(amostra("host", 0, i))
        cliente.append(amostra("client", 1, i))
    host.extend(serie_pos("host", 0))
    cliente.extend(serie_pos("client", 1))
    host.append(evento("host", 0, "spawn_done", TICK0, T0, bodies=str(CORPOS)))
    cliente.append(
        evento("client", 1, "late_join_done", TICK_LATE, T0 + 2,
               elapsed_ms="820.0", world_hash=HASH, bodies=str(CORPOS))
    )
    host.append(evento("host", 0, "host_quit", TICK0 + 10 * 60, T0 + 10))
    cliente.append(
        evento("client", 1, "shutdown", TICK0 + 10 * 60, T0 + 10, clean="1", reason="host_lost")
    )
    return {"inst0-host.log": host, "inst1-client.log": cliente}


def gravar(arquivos):
    pasta = tempfile.mkdtemp(prefix="soaktest-")
    for nome, linhas in arquivos.items():
        with open(os.path.join(pasta, nome), "w", encoding="utf-8") as fh:
            fh.write("\n".join(linhas) + "\n")
    return pasta


def rodar(arquivos):
    pasta = gravar(arquivos)
    try:
        codigo, resultados = av.avaliar(pasta, instancias=2, duracao_minima=5.0)
        return codigo, {r.nome: r for r in resultados}
    finally:
        shutil.rmtree(pasta, ignore_errors=True)


# --------------------------------------------------------------------------
# Casos
# --------------------------------------------------------------------------

def caso_base():
    codigo, res = rodar(montar_base())
    reprovadas = [n for n, r in res.items() if r.status == "FAIL"]
    return (codigo == 0 and not reprovadas,
            "codigo=%d reprovadas=%s" % (codigo, reprovadas))


def espera_falha(arquivos, metrica):
    codigo, res = rodar(arquivos)
    if codigo != 1:
        return False, "esperava codigo 1, veio %d" % codigo
    if metrica not in res:
        return False, "metrica '%s' nem foi avaliada (avaliadas: %s)" % (
            metrica, sorted(res))
    if res[metrica].status != "FAIL":
        return False, "'%s' veio %s: %s" % (metrica, res[metrica].status, res[metrica].detalhe)
    return True, "%s: %s" % (metrica, res[metrica].detalhe)


def caso_pasta_vazia():
    return espera_falha({}, "integridade")


def caso_sem_meta():
    a = montar_base()
    a["inst1-client.log"] = [l for l in a["inst1-client.log"] if not l.startswith("[SOAK-META]")]
    return espera_falha(a, "integridade")


def caso_sem_amostras():
    a = montar_base()
    for nome in a:
        a[nome] = [l for l in a[nome] if not l.startswith("[SOAK] ")]
    return espera_falha(a, "integridade")


def caso_dois_hosts():
    a = montar_base()
    a["inst1-client.log"][0] = meta("host", 1)
    return espera_falha(a, "integridade")


def caso_runs_misturadas():
    a = montar_base()
    a["inst1-client.log"][0] = meta("client", 1, run="r999")
    return espera_falha(a, "integridade")


def caso_input_nao_instrumentado():
    """-1 nao pode passar por "respondeu em -1 ms".

    Falso verde real de 17/09: o cliente emitia input_ms_p99=-1 porque o campo nao
    estava instrumentado, e o avaliador devolvia PASS com "pior p99 -1.0 ms
    (limite 100)". O juiz dava verde justamente para o que nao foi medido.
    """
    a = montar_base()
    a["inst1-client.log"] = [meta("client", 1)] + [
        amostra("client", 1, i, input_ms_p99="-1") for i in range(N_AMOSTRAS)
    ] + [
        evento("client", 1, "late_join_done", TICK_LATE, T0 + 2,
               elapsed_ms="820.0", world_hash=HASH, bodies=str(CORPOS)),
        evento("client", 1, "shutdown", TICK0 + 10 * 60, T0 + 10, clean="1", reason="x"),
    ]
    return espera_falha(a, "resposta_do_input")


def sem_linhas_de_posicao(linhas):
    return [l for l in linhas if not l.startswith("[SOAK-POS] ")]


def caso_drift_sem_serie_do_cliente():
    """O cliente nao instrumentou posicao.

    Antes da change 15 isto era o caso do campo `drift_max_u=-1`. O campo deixou de
    existir como medida (nenhuma instancia consegue preenche-lo, ver docs/00), mas a
    armadilha que ele guardava continua: instancia que nao mede nao pode passar.
    """
    a = montar_base()
    a["inst1-client.log"] = sem_linhas_de_posicao(a["inst1-client.log"])
    return espera_falha(a, "drift")


def caso_drift_sem_serie_do_host():
    """Sem a verdade do host nao existe contra o que comparar."""
    a = montar_base()
    a["inst0-host.log"] = sem_linhas_de_posicao(a["inst0-host.log"])
    return espera_falha(a, "drift")


def caso_drift_series_nao_se_cruzam():
    """As duas series existem e nao tem NENHUM ntick em comum.

    Sem este caso, um cruzamento vazio devolveria "pior drift: nenhum" e poderia
    virar PASS — o pior tipo de verde, o de quem nao comparou nada.
    """
    a = montar_base()
    cliente = sem_linhas_de_posicao(a["inst1-client.log"])
    cliente += [
        posicao("client", 1, NTICK0 + 100000 + i, T0 + i / HZ_REDE, pos_do_host(i))
        for i in range(N_POS)
    ]
    a["inst1-client.log"] = cliente
    return espera_falha(a, "drift")


def caso_drift_corrida_curta():
    """Todos os ticks do host antes dos 5 min: o briefing nao permite concluir nada."""
    a = montar_base()
    host = sem_linhas_de_posicao(a["inst0-host.log"])
    host += [
        posicao("host", 0, NTICK0 + i, 10.0 + i / HZ_REDE, pos_do_host(i))
        for i in range(N_POS)
    ]
    a["inst0-host.log"] = host
    return espera_falha(a, "drift")


def caso_cliente_atrasado_mas_correto():
    """Atraso pequeno: PASS, e o avaliador tem que DIZER de quanto foi o atraso.

    2 ticks x 0.05 u = 0.10 u, abaixo do limite de 0.15. O ponto do caso nao e o
    PASS: e que `atraso de 2 tick(s)` apareca no detalhe. Sem isso, um FAIL futuro
    seria anunciado sem poder ser explicado, que e metade da razao de a change 15
    medir dois numeros em vez de um.
    """
    a = montar_base()
    a["inst1-client.log"] = sem_linhas_de_posicao(a["inst1-client.log"]) \
        + serie_pos("client", 1, desloca_ticks=2)
    codigo, res = rodar(a)
    r = res.get("drift")
    if r is None:
        return False, "drift nem foi avaliado"
    if r.status != "PASS":
        return False, "esperava PASS, veio %s: %s" % (r.status, r.detalhe)
    if "atraso de 2 tick(s)" not in r.detalhe:
        return False, "o atraso medido nao apareceu no detalhe: %s" % r.detalhe
    return True, "drift=%s (codigo %d): %s" % (r.status, codigo, r.detalhe)


def caso_relogio_travado():
    """O processo parou de simular no meio, mas seguiu escrevendo amostras.

    Caso real (errors/05 da task): o player do Unity para o loop quando a janela
    perde o foco. As amostras que restam sao todas BOAS -- e por isso a corrida
    passava em tudo. Aqui os ticks param de avancar a partir da 5a amostra
    enquanto t continua andando.
    """
    a = montar_base()
    a["inst0-host.log"] = [meta("host", 0)] + [
        amostra("host", 0, i, tick=str(TICK0 + min(i, 4) * 60))
        for i in range(N_AMOSTRAS)
    ] + [evento("host", 0, "host_quit", TICK0 + 4 * 60, T0 + 10)]
    return espera_falha(a, "relogio_coerente")


def caso_duracao_no_limite():
    """Corrida que cobre exatamente o minimo nao pode reprovar por 1 amostra.

    Cada amostra cobre o intervalo ate a proxima, entao 11 amostras de 1 em 1 s
    cobrem 11 s e nao 10. Antes de 17/09 o avaliador media so o span e reprovava
    a corrida de 600 s exatos do soak.
    """
    codigo, res = rodar(montar_base())
    r = res.get("duracao")
    if r is None:
        return False, "duracao nem foi avaliada"
    # montar_base tem N_AMOSTRAS amostras de 1 em 1 s: cobre N_AMOSTRAS segundos.
    ok = r.status == "PASS"
    return ok, "duracao: %s (%s)" % (r.status, r.detalhe)


def caso_duracao_curta():
    a = {"inst0-host.log": [meta("host", 0), amostra("host", 0, 0), amostra("host", 0, 1),
                            evento("host", 0, "host_quit", TICK0, T0)],
         "inst1-client.log": [meta("client", 1), amostra("client", 1, 0), amostra("client", 1, 1),
                              evento("client", 1, "shutdown", TICK0, T0, clean="1", reason="x")]}
    return espera_falha(a, "duracao")


def caso_fps_baixo():
    a = montar_base()
    a["inst0-host.log"] = [meta("host", 0)] + [
        amostra("host", 0, i, fps="41.0") for i in range(N_AMOSTRAS)
    ] + [evento("host", 0, "host_quit", TICK0 + 600, T0 + 10)]
    return espera_falha(a, "fps_host")


def caso_frame_p99_alto():
    """fps medio bate 60 mas o quadro engasga — 'estavel' tem que pegar isso."""
    a = montar_base()
    a["inst0-host.log"] = [meta("host", 0)] + [
        amostra("host", 0, i, fps="60.5", frame_p99_ms="48.00") for i in range(N_AMOSTRAS)
    ] + [evento("host", 0, "host_quit", TICK0 + 600, T0 + 10)]
    return espera_falha(a, "fps_host")


def caso_teleporte():
    a = montar_base()
    a["inst1-client.log"][5] = amostra("client", 1, 4, carry_jump_u="0.940")
    return espera_falha(a, "viga_nao_teleporta")


def caso_campo_ausente():
    """Campo que sumiu tem que virar FAIL legivel, nunca stack trace nem PASS."""
    a = montar_base()
    novas = []
    for l in a["inst1-client.log"]:
        if l.startswith("[SOAK] "):
            l = " ".join(t for t in l.split() if not t.startswith("carry_jump_u="))
        novas.append(l)
    a["inst1-client.log"] = novas
    a["inst0-host.log"] = [
        " ".join(t for t in l.split() if not t.startswith("carry_jump_u="))
        if l.startswith("[SOAK] ") else l
        for l in a["inst0-host.log"]
    ]
    return espera_falha(a, "viga_nao_teleporta")


def caso_input_lento():
    a = montar_base()
    a["inst1-client.log"][3] = amostra("client", 1, 2, input_ms_p99="163.0")
    return espera_falha(a, "resposta_do_input")


def caso_drift_alto():
    """0.31 u PERPENDICULAR ao movimento: nenhum deslocamento no tempo apaga isso.

    O erro e em y, e a viga anda em x. E o caso que separa divergencia de atraso: se
    a busca de alinhamento pudesse mascarar um erro real, ela mascararia este.
    """
    a = montar_base()
    a["inst1-client.log"] = sem_linhas_de_posicao(a["inst1-client.log"]) \
        + serie_pos("client", 1, offset_y=0.31)
    return espera_falha(a, "drift")


def caso_drift_antes_dos_300s_nao_conta():
    """Guarda contra o avaliador ser severo demais: drift alto ANTES dos 5 min
    e permitido pelo briefing, e reprovar ali seria falso positivo."""
    a = montar_base()
    a["inst1-client.log"] = sem_linhas_de_posicao(a["inst1-client.log"]) \
        + serie_pos("client", 1, so_antes_de=300.0, offset_y_antes=0.9)
    codigo, res = rodar(a)
    ok = res.get("drift") is not None and res["drift"].status == "PASS"
    return ok, "drift=%s (codigo %d): %s" % (
        res["drift"].status if "drift" in res else "ausente", codigo,
        res["drift"].detalhe if "drift" in res else "-")


def trocar_late_join(arquivos, **campos):
    base = {"elapsed_ms": "820.0", "world_hash": HASH, "bodies": str(CORPOS)}
    base.update(campos)
    arquivos["inst1-client.log"] = [
        evento("client", 1, "late_join_done", TICK_LATE, T0 + 2, **base)
        if "late_join_done" in l else l
        for l in arquivos["inst1-client.log"]
    ]
    return arquivos


# --------------------------------------------------------------------------
# Drift com VARIOS corpos (changes/19). Os casos acima usam uma serie so, que
# cai no balde de compatibilidade do avaliador — ou seja, NAO exercitam o
# cruzamento por ObjectId. Estes exercitam.
# --------------------------------------------------------------------------

CORPOS_NA_SERIE = ["11", "12", "13"]


def serie_multi(role, ident, obj_torto=None, erro_y=0.0):
    """Tres corpos, cada um numa reta propria. `obj_torto` recebe erro em y."""
    linhas = []
    for i in range(N_POS):
        ntick = NTICK0 + i
        t = T0 + i / HZ_REDE
        for k, obj in enumerate(CORPOS_NA_SERIE):
            x, y, z = pos_do_host(i)
            # Cada corpo numa faixa de z diferente, para o pior caso poder ser
            # atribuido a um deles sem ambiguidade.
            z += k * 10.0
            if obj == obj_torto:
                y += erro_y
            linhas.append(posicao(role, ident, ntick, t, (x, y, z), obj=obj))
    return linhas


def montar_multi(obj_torto=None, erro_y=0.0):
    a = montar_base()
    a["inst0-host.log"] = sem_linhas_de_posicao(a["inst0-host.log"]) \
        + serie_multi("host", 0)
    a["inst1-client.log"] = sem_linhas_de_posicao(a["inst1-client.log"]) \
        + serie_multi("client", 1, obj_torto=obj_torto, erro_y=erro_y)
    return a


def caso_drift_multi_corpos_ok():
    """Tres corpos, todos certos: PASS, e o avaliador tem que dizer que sao TRES.

    Sem esta conferencia, um cruzamento que casasse so um corpo passaria igual — e
    seria um verde obtido por comparar 1/3 do mundo.
    """
    codigo, res = rodar(montar_multi())
    r = res.get("drift")
    if r is None:
        return False, "drift nem foi avaliado"
    if r.status != "PASS":
        return False, "esperava PASS, veio %s: %s" % (r.status, r.detalhe)
    if "3 corpo(s)" not in r.detalhe:
        return False, "nao comparou os 3 corpos: %s" % r.detalhe
    return True, "drift=%s (codigo %d): %s" % (r.status, codigo, r.detalhe)


def caso_drift_uma_caixa_fora_do_lugar():
    """UM corpo entre tres esta errado. Tem que reprovar E dizer qual.

    E o caso que o drift de um corpo so nao pegava: com 150 caixas replicando, uma
    delas no lugar errado nao aparece em metrica nenhuma que olhe so para a viga.
    """
    a = montar_multi(obj_torto="12", erro_y=0.40)
    codigo, res = rodar(a)
    r = res.get("drift")
    if r is None:
        return False, "drift nem foi avaliado"
    if r.status != "FAIL":
        return False, "esperava FAIL, veio %s: %s" % (r.status, r.detalhe)
    if "obj=12" not in r.detalhe:
        return False, "reprovou sem dizer qual corpo: %s" % r.detalhe
    return True, "drift: %s" % r.detalhe


def caso_late_join_incompleto():
    """O cliente recebeu MENOS corpos do que o host criou.

    E o que "recebe estado completo e correto" do briefing quer dizer na parte
    'completo'. Caso real: hoje a candidata B replica so a viga, e o cliente emite
    bodies=1 contra os 151 do host — este caso e o espelho sintetico disso.
    """
    return espera_falha(trocar_late_join(montar_base(), bodies="140"), "late_join")


def caso_late_join_sem_bodies():
    """Evento sem o campo: nao da para afirmar completude, entao nao passa."""
    a = montar_base()
    a["inst1-client.log"] = [
        evento("client", 1, "late_join_done", TICK_LATE, T0 + 2,
               elapsed_ms="820.0", world_hash=HASH)
        if "late_join_done" in l else l
        for l in a["inst1-client.log"]
    ]
    return espera_falha(a, "late_join")


def caso_late_join_sem_spawn_done():
    """Sem saber quantos corpos o mundo tem, 'completo' nao tem contra o que ser medido.

    Guarda contra o pior verde possivel: o avaliador nao pode aprovar completude
    comparando o cliente com nada.
    """
    a = montar_base()
    a["inst0-host.log"] = [l for l in a["inst0-host.log"] if "ev=spawn_done" not in l]
    return espera_falha(a, "late_join")


def caso_late_join_hash_diferente_nao_reprova():
    """Guarda contra a regra ANTIGA voltar.

    O cliente renderiza interpolado, alguns ticks atras do host (medido: 5). O hash
    quantiza a 0.01 u e a viga anda ~0.108 u por tick — um unico tick de diferenca ja
    muda o hash. Exigir hash igual num instante e exigir latencia zero, e reprovava
    replicacao correta. Quem responde 'esta no lugar certo?' e o drift.
    """
    a = trocar_late_join(montar_base(), world_hash="deadbeef")
    codigo, res = rodar(a)
    r = res.get("late_join")
    if r is None:
        return False, "late_join nem foi avaliado"
    if r.status != "PASS":
        return False, "hash diferente reprovou de novo: %s" % r.detalhe
    return True, "late_join=%s (codigo %d): %s" % (r.status, codigo, r.detalhe)


def caso_sem_host_quit():
    a = montar_base()
    a["inst0-host.log"] = [l for l in a["inst0-host.log"] if "host_quit" not in l]
    return espera_falha(a, "queda_do_host")


def caso_shutdown_sujo():
    a = montar_base()
    a["inst1-client.log"] = [
        evento("client", 1, "shutdown", TICK0 + 600, T0 + 10,
               clean="0", reason="NullReferenceException")
        if "ev=shutdown" in l else l
        for l in a["inst1-client.log"]
    ]
    return espera_falha(a, "queda_do_host")


def caso_cliente_calado_no_shutdown():
    a = montar_base()
    a["inst1-client.log"] = [l for l in a["inst1-client.log"] if "ev=shutdown" not in l]
    return espera_falha(a, "queda_do_host")


def caso_excecao():
    a = montar_base()
    a["inst1-client.log"].append(
        evento("client", 1, "exception", TICK0 + 300, T0 + 5, where="CarryJoint.Update")
    )
    return espera_falha(a, "zero_excecoes")


CASOS = [
    ("base passa limpo", caso_base),
    ("pasta vazia", caso_pasta_vazia),
    ("log sem cabecalho META", caso_sem_meta),
    ("nenhuma amostra", caso_sem_amostras),
    ("dois hosts", caso_dois_hosts),
    ("logs de corridas diferentes", caso_runs_misturadas),
    ("corrida curta demais", caso_duracao_curta),
    ("input nao instrumentado (-1)", caso_input_nao_instrumentado),
    ("drift: cliente sem serie de posicao", caso_drift_sem_serie_do_cliente),
    ("drift: host sem serie de posicao", caso_drift_sem_serie_do_host),
    ("drift: series sem nenhum ntick em comum", caso_drift_series_nao_se_cruzam),
    ("drift: corrida nao chegou aos 5 min", caso_drift_corrida_curta),
    ("drift: cliente atrasado mas correto", caso_cliente_atrasado_mas_correto),
    ("drift: 3 corpos, todos certos", caso_drift_multi_corpos_ok),
    ("drift: 1 caixa entre 3 fora do lugar", caso_drift_uma_caixa_fora_do_lugar),
    ("relogio travado no meio", caso_relogio_travado),
    ("duracao exatamente no limite", caso_duracao_no_limite),
    ("fps do host abaixo de 60", caso_fps_baixo),
    ("fps ok mas p99 do quadro estourado", caso_frame_p99_alto),
    ("viga teleporta", caso_teleporte),
    ("campo carry_jump_u ausente", caso_campo_ausente),
    ("input acima de 100 ms", caso_input_lento),
    ("drift acima de 0.15 depois dos 5 min", caso_drift_alto),
    ("drift alto ANTES dos 5 min nao reprova", caso_drift_antes_dos_300s_nao_conta),
    ("late join com estado incompleto", caso_late_join_incompleto),
    ("late join sem campo bodies", caso_late_join_sem_bodies),
    ("late join sem spawn_done do host", caso_late_join_sem_spawn_done),
    ("late join: hash diferente NAO reprova", caso_late_join_hash_diferente_nao_reprova),
    ("host nunca emitiu host_quit", caso_sem_host_quit),
    ("cliente encerrou com clean=0", caso_shutdown_sujo),
    ("cliente nao emitiu shutdown", caso_cliente_calado_no_shutdown),
    ("uma excecao na corrida", caso_excecao),
]


def main():
    largura = max(len(n) for n, _ in CASOS)
    passou = 0
    for nome, fn in CASOS:
        try:
            ok, detalhe = fn()
        except Exception as erro:  # noqa: BLE001 — o teste nunca pode explodir
            ok, detalhe = False, "EXCECAO no proprio teste: %r" % erro
        print("%-4s %-*s  %s" % ("ok" if ok else "FALHOU", largura, nome, detalhe))
        passou += 1 if ok else 0
    print("-" * 78)
    print("%d/%d" % (passou, len(CASOS)))
    return 0 if passou == len(CASOS) else 1


if __name__ == "__main__":
    sys.exit(main())
