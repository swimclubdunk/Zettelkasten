# Downloads the all-MiniLM-L6-v2 embedding model once. The app itself never touches the network.
$ErrorActionPreference = 'Stop'
$models = Join-Path (Split-Path -Parent $PSScriptRoot) 'models'
New-Item -ItemType Directory -Force $models | Out-Null
$base = 'https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main'

$vocab = Join-Path $models 'vocab.txt'
if (-not (Test-Path $vocab)) { Invoke-WebRequest "$base/vocab.txt" -OutFile $vocab }

$model = Join-Path $models 'model.onnx'
if (-not (Test-Path $model)) {
    try {
        # ~23 MB int8-quantized variant
        Invoke-WebRequest "$base/onnx/model_quint8_avx2.onnx" -OutFile $model
    } catch {
        Write-Warning "Quantized model unavailable ($($_.Exception.Message)); downloading full model (~90 MB)."
        Invoke-WebRequest "$base/onnx/model.onnx" -OutFile $model
    }
}
Get-ChildItem $models | Select-Object Name, Length
