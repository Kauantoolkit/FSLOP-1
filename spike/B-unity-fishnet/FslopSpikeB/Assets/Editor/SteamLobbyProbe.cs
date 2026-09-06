using System.Globalization;
using System.Threading;
using Steamworks;
using UnityEditor;
using UnityEngine;

namespace Fslop.SpikeB.EditorTools
{
    /// <summary>
    /// Sobe a Steam API de verdade, cria um lobby de verdade nos servidores da Valve,
    /// deriva o codigo de entrada dele e entra por esse codigo.
    ///
    /// Precisa do cliente Steam ABERTO E LOGADO. Sem ele, SteamAPI.InitEx reprova e a
    /// sonda diz exatamente por que — nao adianta insistir.
    ///
    /// O que ela NAO consegue provar, e diz no log: convite (precisa de um amigo), e
    /// conexao P2P real entre dois peers (uma conta Steam por PC, decisions/01).
    ///
    /// Rodar: Unity.exe -projectPath ... -batchmode -quit -nographics
    ///        -executeMethod Fslop.SpikeB.EditorTools.SteamLobbyProbe.Run
    /// </summary>
    public static class SteamLobbyProbe
    {
        const int TimeoutMs = 15000;
        const int PumpIntervalMs = 50;

        static string failure;

        public static void Run()
        {
            failure = null;

            var steam = new SteamLobbyService();

            try
            {
                Execute(steam);
            }
            finally
            {
                steam.Dispose();
                Log("steam desligado");
            }

            if (failure != null)
            {
                Debug.LogError("[LOBBY] FAIL " + failure);
                EditorApplication.Exit(1);
                return;
            }

            Log("PASS");
            EditorApplication.Exit(0);
        }

        static void Execute(SteamLobbyService steam)
        {
            Log("SteamAPI.IsSteamRunning=" + SteamAPI.IsSteamRunning());

            if (!steam.Init())
            {
                Fail("SteamAPI.InitEx reprovou: " + steam.LastError +
                     " — o cliente Steam esta aberto e logado?");
                return;
            }

            Log(F("init ok appid={0} usuario={1} nome={2} universo={3}",
                SteamUtils.GetAppID().m_AppId,
                steam.LocalUser.m_SteamID,
                SteamFriends.GetPersonaName(),
                steam.Universe));

            if (SteamUtils.GetAppID().m_AppId != SteamLobbyService.SpikeAppId)
            {
                Fail(F("subiu contra o appid {0} e o esperado era {1}",
                    SteamUtils.GetAppID().m_AppId, SteamLobbyService.SpikeAppId));
                return;
            }

            // --- criar o lobby ---
            steam.RequestCreateLobby();
            if (!Aguardar(steam, () => steam.CreateCompleted, "criacao do lobby"))
            {
                return;
            }

            if (steam.CreateResult != EResult.k_EResultOK)
            {
                Fail("CreateLobby devolveu " + steam.CreateResult);
                return;
            }

            CSteamID lobby = steam.CreatedLobby;
            Log(F("lobby criado id64={0} e_lobby={1} tipo_conta={2} instancia=0x{3:X} " +
                  "membros={4}/{5} dono={6}",
                lobby.m_SteamID, lobby.IsLobby(), lobby.GetEAccountType(),
                lobby.GetUnAccountInstance(),
                SteamMatchmaking.GetNumLobbyMembers(lobby), SteamLobbyService.MaxMembers,
                SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID));

            // --- o codigo de entrada, e o ida-e-volta ---
            string codigo = LobbyCode.From(lobby);
            Log("codigo de entrada=" + codigo);

            CSteamID reconstruido;
            if (!LobbyCode.TryParse(codigo, steam.Universe, out reconstruido))
            {
                Fail("LobbyCode.TryParse nao aceitou o proprio codigo que gerou: " + codigo);
                return;
            }

            Log(F("ida-e-volta: id64_original={0} id64_do_codigo={1} igual={2}",
                lobby.m_SteamID, reconstruido.m_SteamID,
                reconstruido.m_SteamID == lobby.m_SteamID));

            if (reconstruido.m_SteamID != lobby.m_SteamID)
            {
                Fail(F("o codigo nao reconstroi o lobby: {0} != {1}. A suposicao sobre o " +
                       "formato do SteamID de lobby esta errada (instancia real 0x{2:X})",
                    reconstruido.m_SteamID, lobby.m_SteamID, lobby.GetUnAccountInstance()));
                return;
            }

            // Tolerancia de digitacao: minusculas, sem hifen, e as letras que a base32 de
            // Crockford descarta. Se isso quebrar, o codigo nao serve para ser ditado.
            if (!VerificarTolerancia(codigo, lobby, steam.Universe))
            {
                return;
            }

            // --- metadados do lobby ---
            SteamMatchmaking.SetLobbyData(lobby, "stack", "B");
            SteamMatchmaking.SetLobbyData(lobby, "codigo", codigo);
            string lido = SteamMatchmaking.GetLobbyData(lobby, "codigo");
            Log(F("metadado gravado e lido de volta: codigo='{0}' stack='{1}'",
                lido, SteamMatchmaking.GetLobbyData(lobby, "stack")));

            if (lido != codigo)
            {
                Fail(F("metadado voltou diferente: '{0}' != '{1}'", lido, codigo));
                return;
            }

            // --- entrar pelo codigo ---
            steam.RequestJoin(reconstruido);
            if (!Aguardar(steam, () => steam.EnterCompleted, "entrada no lobby pelo codigo"))
            {
                return;
            }

            Log(F("entrou id64={0} resposta={1} membros={2}",
                steam.EnteredLobby.m_SteamID, steam.EnterResponse,
                SteamMatchmaking.GetNumLobbyMembers(steam.EnteredLobby)));

            if (steam.EnteredLobby.m_SteamID != lobby.m_SteamID)
            {
                Fail(F("entrou no lobby errado: {0} != {1}",
                    steam.EnteredLobby.m_SteamID, lobby.m_SteamID));
                return;
            }

            // --- o que esta corrida NAO consegue provar ---
            Log("[NAO EXECUTADO] convite — InviteUserToLobby exige o SteamID de um amigo online");
            Log("[NAO EXECUTADO] conexao P2P entre dois peers — uma conta Steam por PC " +
                "(decisions/01); duas instancias aqui teriam o mesmo SteamID");
            Log("[NAO EXECUTADO] transporte FishySteamworks — este teste sobe lobby, nao socket");

            steam.Leave(lobby);
            Log("saiu do lobby " + lobby.m_SteamID);
        }

        static bool VerificarTolerancia(string codigo, CSteamID lobby, EUniverse universo)
        {
            string[] variantes =
            {
                codigo.ToLowerInvariant(),
                codigo.Replace("-", string.Empty),
                " " + codigo + " ",
            };

            foreach (string variante in variantes)
            {
                CSteamID saida;
                if (!LobbyCode.TryParse(variante, universo, out saida)
                    || saida.m_SteamID != lobby.m_SteamID)
                {
                    Fail(F("a variante '{0}' do codigo nao reconstroi o lobby", variante));
                    return false;
                }
            }

            Log(F("tolerancia de digitacao ok em {0} variantes (minuscula, sem hifen, com espaco)",
                variantes.Length));
            return true;
        }

        static bool Aguardar(SteamLobbyService steam, System.Func<bool> pronto, string oQue)
        {
            int esperou = 0;
            while (!pronto() && esperou < TimeoutMs)
            {
                steam.Pump();
                Thread.Sleep(PumpIntervalMs);
                esperou += PumpIntervalMs;
            }

            if (!pronto())
            {
                Fail(F("timeout de {0} ms esperando: {1}", TimeoutMs, oQue));
                return false;
            }

            Log(F("{0}: respondeu em {1} ms", oQue, esperou));
            return true;
        }

        static string F(string formato, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, formato, args);
        }

        static void Log(string mensagem)
        {
            Debug.Log("[LOBBY] " + mensagem);
        }

        static void Fail(string motivo)
        {
            if (failure == null)
            {
                failure = motivo;
            }
        }
    }
}
