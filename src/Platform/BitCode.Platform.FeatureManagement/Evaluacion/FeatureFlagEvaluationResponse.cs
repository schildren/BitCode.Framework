namespace BitCode.Framework.Platform.FeatureManagement.Evaluacion;

/// <summary><see cref="MotivoActivo"/> documenta CUÁL de las tres reglas de evaluación decidió el
/// resultado -- útil para depurar por qué un usuario concreto ve (o no) una funcionalidad, sin tener que
/// inspeccionar los datos crudos de segmentos/rollouts.</summary>
public sealed record FeatureFlagEvaluationResponse(string Nombre, bool Activo, string Motivo);
