namespace VSAI.AILogic
{
    internal sealed class StickyAimSelector
    {
        private const int MaxFramesWithoutTarget = 3;
        private const float LockScoreDecay = 0.85f;
        private const float LockScoreGain = 15f;
        private const float MaxLockScore = 100f;
        private const float ReferenceTargetSize = 10000f;

        private Prediction? _currentTarget;
        private int _consecutiveFramesWithoutTarget;
        private float _lastTargetVelocityX;
        private float _lastTargetVelocityY;
        private float _targetLockScore;
        private int _framesWithoutMatch;

        internal Prediction? SelectTarget(
            bool stickyAimEnabled,
            float stickyThreshold,
            int imageSize,
            Prediction? bestCandidate,
            IReadOnlyList<Prediction> predictions,
            out bool isPersistentLock)
        {
            isPersistentLock = false;

            if (!stickyAimEnabled)
            {
                Reset();
                return bestCandidate;
            }

            // --- TRAVA DE ALVO ÚNICO (HARD LOCK) ---
            if (_currentTarget != null)
            {
                Prediction? matchedTarget = null;
                float bestDistSq = float.MaxValue;
                
                // Limites retangulares de trava de alvo escalados conforme a imagem do modelo
                float lockRadiusX = 80f * (imageSize / 320f);
                float lockRadiusY = 240f * (imageSize / 320f);

                foreach (var candidate in predictions)
                {
                    // Compara a posição atual do candidato com a última posição registrada da trava
                    float dx = Math.Abs(candidate.ScreenCenterX - _currentTarget.ScreenCenterX);
                    float dy = Math.Abs(candidate.ScreenCenterY - _currentTarget.ScreenCenterY);

                    // Verifica se o candidato está dentro da caixa de trava retangular (eixo Y livre)
                    if (dx < lockRadiusX && dy < lockRadiusY)
                    {
                        // Distância ponderada: o eixo Y tem apenas 5% de peso para permitir que o usuário mova o mouse em Y livremente
                        // sem perder o lock do alvo, mas ainda serve para desempate entre múltiplos candidatos
                        float distSq = dx * dx + (dy * dy * 0.05f);
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            matchedTarget = candidate;
                        }
                    }
                }

                if (matchedTarget != null)
                {
                    _framesWithoutMatch = 0;
                    _consecutiveFramesWithoutTarget = 0;
                    
                    float targetArea = matchedTarget.Rectangle.Width * matchedTarget.Rectangle.Height;
                    float sizeFactor = GetSizeFactor(targetArea);
                    UpdateVelocity(matchedTarget, sizeFactor);
                    
                    _currentTarget = matchedTarget;
                    isPersistentLock = true;
                    return matchedTarget;
                }

                // Se perdeu a detecção neste frame, tenta prever a posição baseando-se na velocidade por até 3 frames
                _framesWithoutMatch++;
                if (_framesWithoutMatch < 3)
                {
                    var predictedTarget = HandleNoDetections();
                    if (predictedTarget != null)
                    {
                        isPersistentLock = true;
                        return predictedTarget;
                    }
                }

                // Perdeu o alvo de vez, reseta para buscar um novo
                Reset();
            }

            // --- BUSCA DE NOVO ALVO ---
            if (bestCandidate == null || predictions.Count == 0)
            {
                return HandleNoDetections();
            }

            _consecutiveFramesWithoutTarget = 0;

            float screenCenterX = imageSize / 2f;
            float screenCenterY = imageSize / 2f;
            Prediction? aimTarget = null;
            float nearestToCrosshairDistSq = float.MaxValue;

            foreach (var candidate in predictions)
            {
                float distSq = GetDistanceSq(candidate.ScreenCenterX, candidate.ScreenCenterY, screenCenterX, screenCenterY);
                if (distSq < nearestToCrosshairDistSq)
                {
                    nearestToCrosshairDistSq = distSq;
                    aimTarget = candidate;
                }
            }

            if (aimTarget == null)
            {
                return HandleNoDetections();
            }

            isPersistentLock = false;
            return AcquireNewTarget(aimTarget);
        }

        private static float GetDistanceSq(float x1, float y1, float x2, float y2)
        {
            float dx = x1 - x2;
            float dy = y1 - y2;
            return dx * dx + dy * dy;
        }

        private static float GetSizeFactor(float targetArea)
        {
            float ratio = ReferenceTargetSize / Math.Max(targetArea, 100f);
            return Math.Clamp(ratio, 1.0f, 3.0f);
        }

        private Prediction? HandleNoDetections()
        {
            if (_currentTarget != null && ++_consecutiveFramesWithoutTarget <= MaxFramesWithoutTarget)
            {
                _targetLockScore *= LockScoreDecay;

                return new Prediction
                {
                    ScreenCenterX = _currentTarget.ScreenCenterX + _lastTargetVelocityX * _consecutiveFramesWithoutTarget,
                    ScreenCenterY = _currentTarget.ScreenCenterY + _lastTargetVelocityY * _consecutiveFramesWithoutTarget,
                    Rectangle = _currentTarget.Rectangle,
                    Confidence = _currentTarget.Confidence * (1f - _consecutiveFramesWithoutTarget * 0.2f),
                    ClassId = _currentTarget.ClassId,
                    ClassName = _currentTarget.ClassName,
                    CenterXTranslated = _currentTarget.CenterXTranslated,
                    CenterYTranslated = _currentTarget.CenterYTranslated
                };
            }

            Reset();
            return null;
        }

        private Prediction AcquireNewTarget(Prediction target)
        {
            _lastTargetVelocityX = 0f;
            _lastTargetVelocityY = 0f;
            _targetLockScore = LockScoreGain;
            _framesWithoutMatch = 0;
            _currentTarget = target;
            return target;
        }

        private void UpdateVelocity(Prediction newTarget, float sizeFactor)
        {
            if (_currentTarget == null)
            {
                return;
            }

            float smoothing = Math.Clamp(0.6f + sizeFactor * 0.1f, 0.7f, 0.9f);
            float newWeight = 1f - smoothing;

            float newVelX = newTarget.ScreenCenterX - _currentTarget.ScreenCenterX;
            float newVelY = newTarget.ScreenCenterY - _currentTarget.ScreenCenterY;
            _lastTargetVelocityX = _lastTargetVelocityX * smoothing + newVelX * newWeight;
            _lastTargetVelocityY = _lastTargetVelocityY * smoothing + newVelY * newWeight;
        }

        internal void Reset()
        {
            _currentTarget = null;
            _consecutiveFramesWithoutTarget = 0;
            _framesWithoutMatch = 0;
            _lastTargetVelocityX = 0f;
            _lastTargetVelocityY = 0f;
            _targetLockScore = 0f;
        }
    }
}
