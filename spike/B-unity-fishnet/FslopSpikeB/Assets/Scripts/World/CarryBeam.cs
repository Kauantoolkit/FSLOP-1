using System.Collections.Generic;
using UnityEngine;

namespace Fslop.SpikeB
{
    /// <summary>
    /// A viga rigida de 6 m que ate 4 jogadores carregam juntos — o requisito mais dificil
    /// do briefing.
    ///
    /// O agarre e uma MOLA, nao um vinculo rigido (decisions/09, abordagem 4). A razao e de
    /// rede, nao de gosto: um vinculo rigido obrigaria o cliente a predizer a viga junto com
    /// o personagem, porque senao a mao predita e a viga replicada ficam em tempos
    /// diferentes e a viga treme. Com mola, a viga JA fica para tras da mao em jogo local —
    /// ela e pesada e a mola estica — e o atraso da rede se esconde dentro desse atraso.
    /// Assim o cliente nao precisa de joint nenhum: a viga fica so no host.
    ///
    /// Este arquivo NAO sabe nada de rede. Ele so cria e destroi molas; quem decide que o
    /// joint existe apenas no host e a camada de rede, na change seguinte.
    /// </summary>
    public class CarryBeam : MonoBehaviour
    {
        /// <summary>6 m, do briefing. Nao e escolha.</summary>
        public const float LengthU = 6f;

        [SerializeField] float thickness = 0.5f;

        /// <summary>
        /// PLACEHOLDER declarado. 120 kg contra os 70 kg de um jogador e o que faz "ate 4"
        /// significar alguma coisa — com 20 kg um jogador sozinho resolveria e o requisito
        /// perderia o sentido. Quanto exatamente e design; a sonda mede quantas maos
        /// levantam a viga com cada valor, para a escolha ser feita com numero.
        /// </summary>
        [SerializeField] float massKg = 120f;

        [SerializeField] float spring = 4000f;
        [SerializeField] float damper = 200f;

        /// <summary>Folga da mola: abaixo disso ela nao puxa. Da o "solto" do agarre.</summary>
        [SerializeField] float maxDistance = 0.05f;

        /// <summary>
        /// Os dois lados do dial de decisions/09: quanto a viga puxa o jogador de volta.
        /// O Unity chama isso de massScale, e o que ele escala e a massa INVERSA — entao a
        /// direcao do efeito nao e obvia pelo nome. A sonda mede o deslocamento da mao e da
        /// viga com valores diferentes, em vez de eu afirmar qual lado e qual.
        /// </summary>
        [SerializeField] float holderMassScale = 1f;
        [SerializeField] float beamMassScale = 1f;

        /// <summary>Onde fica a "mao" na capsula: na altura do peito e um pouco a frente.</summary>
        [SerializeField] Vector3 handAnchor = new Vector3(0f, 0.5f, 0.5f);

        /// <summary>
        /// Exposto porque a sonda precisa do ponto da mao em coordenadas de mundo para medir
        /// a FOLGA entre mao e viga. Sem isso ela remontaria o offset por fora, e mediria um
        /// ponto que nao e o que o joint usa.
        /// </summary>
        public Vector3 HandAnchor => handAnchor;

        readonly List<SpringJoint> joints = new List<SpringJoint>();

        Rigidbody body;

        public Rigidbody Body
        {
            get
            {
                if (body == null)
                {
                    body = GetComponent<Rigidbody>();
                }

                return body;
            }
        }

        public float MassKg => massKg;
        public float Spring => spring;
        public float Damper => damper;
        public int HolderCount => joints.Count;

        /// <summary>
        /// Prende a mao de um portador ao ponto da viga mais proximo dela. O ponto e preso
        /// ao EIXO da viga (y e z locais zerados): agarrar a quina faria a viga girar em
        /// torno de um ponto fora do eixo e mascarar o que se quer medir.
        /// </summary>
        public SpringJoint Grab(Rigidbody holder, Vector3 handWorld)
        {
            Vector3 local = transform.InverseTransformPoint(handWorld);
            float meia = Mathf.Max(LengthU * 0.5f - thickness * 0.5f, 0f);

            local.x = Mathf.Clamp(local.x, -meia, meia);
            local.y = 0f;
            local.z = 0f;

            var joint = holder.gameObject.AddComponent<SpringJoint>();
            joint.connectedBody = Body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = handAnchor;
            joint.connectedAnchor = local;
            joint.spring = spring;
            joint.damper = damper;
            joint.minDistance = 0f;
            joint.maxDistance = maxDistance;
            joint.massScale = holderMassScale;
            joint.connectedMassScale = beamMassScale;

            // Sem colisao entre a mao e a viga: o jogador esta segurando, nao esbarrando.
            joint.enableCollision = false;

            joints.Add(joint);
            return joint;
        }

        public void ReleaseAll()
        {
            foreach (var joint in joints)
            {
                if (joint == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(joint);
                }
                else
                {
                    DestroyImmediate(joint);
                }
            }

            joints.Clear();
        }

        /// <summary>Ajustes que a sonda varia entre corridas para medir o efeito de cada um.</summary>
        public void Configure(float massKgValue, float springValue, float damperValue,
            float holderMassScaleValue, float beamMassScaleValue)
        {
            massKg = massKgValue;
            spring = springValue;
            damper = damperValue;
            holderMassScale = holderMassScaleValue;
            beamMassScale = beamMassScaleValue;

            Body.mass = massKg;
        }

        public Vector3 AnchorFor(int index, int total)
        {
            // Maos distribuidas ao longo do eixo, simetricas em torno do centro.
            float meia = LengthU * 0.5f - thickness;
            float t = total <= 1 ? 0.5f : index / (float)(total - 1);

            return transform.TransformPoint(new Vector3(Mathf.Lerp(-meia, meia, t), 0f, 0f));
        }
    }
}
