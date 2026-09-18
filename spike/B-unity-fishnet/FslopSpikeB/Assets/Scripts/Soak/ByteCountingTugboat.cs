using System;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;

namespace Fslop.SpikeB
{
    /// <summary>
    /// Tugboat que CONTA os bytes que passam por ele.
    ///
    /// INSTRUMENTO (decisions/12). Existe porque "banda por cliente: reporte media e pico em
    /// KB/s" e metrica do briefing e era a unica sem numero nenhum. O FishNet 4.7.3 nao
    /// expoe contagem: NetworkTrafficStatistics.cs inteiro esta sob
    /// `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, e as classes que guardam os contadores sao
    /// `internal` ao assembly FishNet.Runtime — inalcancaveis de Assembly-CSharp em QUALQUER
    /// build (docs/99 item 17).
    ///
    /// Por HERANCA e nao por decorador: Tugboat nao e sealed e os quatro pontos por onde o
    /// trafego passa sao `virtual` e publicos. Um decorador exigiria delegar ~20 membros
    /// abstratos e reescrever a cada mudanca de API do Transport; isto sao quatro overrides
    /// que chamam base.
    ///
    /// O QUE ESTE NUMERO NAO E, e precisa ir junto dele sempre: sao os bytes de PAYLOAD
    /// entregues ao transporte e recebidos dele. Nao inclui cabecalho UDP/IP (28 bytes por
    /// datagrama), nem o enquadramento e os acks do proprio Tugboat, nem retransmissao. O
    /// numero real no fio e MAIOR. Publicar isto como "banda" sem a ressalva seria publicar
    /// um piso vestido de medida.
    /// </summary>
    public class ByteCountingTugboat : Tugboat
    {
        long bytesSent;
        long bytesReceived;

        /// <summary>Bytes de payload enviados desde o ultimo <see cref="TakeAndReset"/>.</summary>
        public long BytesSent => bytesSent;

        /// <summary>Bytes de payload recebidos desde o ultimo <see cref="TakeAndReset"/>.</summary>
        public long BytesReceived => bytesReceived;

        /// <summary>
        /// Le e zera os dois contadores de uma vez. Ler e zerar em chamadas separadas
        /// perderia o que chegasse entre as duas — a 30 Hz e com o socket noutra thread,
        /// isso nao e hipotetico.
        /// </summary>
        public void TakeAndReset(out long sent, out long received)
        {
            sent = System.Threading.Interlocked.Exchange(ref bytesSent, 0L);
            received = System.Threading.Interlocked.Exchange(ref bytesReceived, 0L);
        }

        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            System.Threading.Interlocked.Add(ref bytesSent, segment.Count);
            base.SendToServer(channelId, segment);
        }

        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            System.Threading.Interlocked.Add(ref bytesSent, segment.Count);
            base.SendToClient(channelId, segment, connectionId);
        }

        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
        {
            System.Threading.Interlocked.Add(ref bytesReceived, receivedDataArgs.Data.Count);
            base.HandleClientReceivedDataArgs(receivedDataArgs);
        }

        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
        {
            System.Threading.Interlocked.Add(ref bytesReceived, receivedDataArgs.Data.Count);
            base.HandleServerReceivedDataArgs(receivedDataArgs);
        }
    }
}
