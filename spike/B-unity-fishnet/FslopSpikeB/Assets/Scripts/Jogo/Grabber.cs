using UnityEngine;

namespace Fslop.SpikeB.Jogo
{
    /// <summary>
    /// Agarra QUALQUER rigidbody por perto, nao so a viga.
    ///
    /// ESTE DIRETORIO NAO E O SPIKE. O briefing proibe "adicionar mecanica de jogo", e o
    /// usuario pediu explicitamente, em 19/09, um brinquedo para sentir se tem graca. A
    /// separacao em Assets/Scripts/Jogo/ e cena propria existe para os numeros da Fase 1
    /// nao serem contaminados por isto — nenhum arquivo do spike referencia nada daqui.
    ///
    /// O agarre e o mesmo da viga, e de proposito: mola de mao unica (decisions/09),
    /// SpringJoint com os mesmos valores. Reusar em vez de inventar um segundo agarre
    /// mantem o que foi medido valendo — se a caixa parecer diferente da viga, e por causa
    /// da massa (10 kg contra 120), nao por causa de um segundo conjunto de numeros que
    /// ninguem mediu.
    ///
    /// Os valores de mola SAO numeros de sensacao e continuam sendo do usuario
    /// (docs/99 item 9). Aqui eles sao herdados, nao escolhidos.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grabber : MonoBehaviour
    {
        /// <summary>Alcance do agarre, a partir do ponto da mao.</summary>
        [SerializeField] float reach = 1.6f;

        /// <summary>Onde fica a mao, em espaco local. Mesmo ponto que a viga usa.</summary>
        [SerializeField] Vector3 handAnchor = new Vector3(0f, 0.5f, 0.5f);

        [SerializeField] float spring = 4000f;
        [SerializeField] float damper = 200f;
        [SerializeField] float maxDistance = 0.05f;

        Rigidbody body;
        SpringJoint joint;

        /// <summary>O que esta na mao agora, ou null.</summary>
        public Rigidbody Held => joint == null ? null : joint.connectedBody;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        public void Toggle()
        {
            if (joint != null)
            {
                Release();
            }
            else
            {
                TryGrab();
            }
        }

        public void Release()
        {
            if (joint != null)
            {
                Destroy(joint);
                joint = null;
            }
        }

        bool TryGrab()
        {
            Vector3 mao = transform.TransformPoint(handAnchor);

            Rigidbody alvo = null;
            float melhor = float.MaxValue;

            foreach (var col in Physics.OverlapSphere(mao, reach))
            {
                var candidato = col.attachedRigidbody;

                // Nao agarra a si mesmo, nem outro jogador, nem corpo estatico. Agarrar
                // outro jogador seria mecanica nova — e mecanica nova nao e minha.
                if (candidato == null || candidato == body || candidato.isKinematic)
                {
                    continue;
                }

                if (candidato.GetComponent<CapsuleMotor>() != null)
                {
                    continue;
                }

                float d = Vector3.SqrMagnitude(candidato.worldCenterOfMass - mao);
                if (d < melhor)
                {
                    melhor = d;
                    alvo = candidato;
                }
            }

            if (alvo == null)
            {
                return false;
            }

            joint = gameObject.AddComponent<SpringJoint>();
            joint.connectedBody = alvo;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = handAnchor;
            joint.connectedAnchor = alvo.transform.InverseTransformPoint(alvo.worldCenterOfMass);
            joint.spring = spring;
            joint.damper = damper;
            joint.minDistance = 0f;
            joint.maxDistance = maxDistance;

            // Sem colisao entre mao e objeto: esta segurando, nao esbarrando. Mesma escolha
            // do CarryBeam, pelo mesmo motivo.
            joint.enableCollision = false;

            return true;
        }
    }
}
