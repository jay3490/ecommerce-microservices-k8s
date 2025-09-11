{{/*
Labels for a workload. Called with a dict: root (the chart context) and name (the workload).
*/}}
{{- define "ecommerce.selectorLabels" -}}
app.kubernetes.io/name: {{ .name }}
{{- end }}

{{- define "ecommerce.labels" -}}
{{ include "ecommerce.selectorLabels" . }}
app.kubernetes.io/part-of: ecommerce
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/managed-by: {{ .root.Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .root.Chart.Name .root.Chart.Version }}
{{- end }}

{{/*
Image reference: <registry><repository>/<workload>:<tag>, with an optional per-workload tag.
*/}}
{{- define "ecommerce.image" -}}
{{- $image := .root.Values.image -}}
{{- printf "%s%s/%s:%s" $image.registry $image.repository .name (default $image.tag .svc.tag) -}}
{{- end }}

{{/*
Whether a workload gets a HorizontalPodAutoscaler: on unless it sets autoscaling: false.
*/}}
{{- define "ecommerce.autoscaled" -}}
{{- $enabled := .root.Values.autoscaling.enabled -}}
{{- if hasKey .svc "autoscaling" -}}
{{- $enabled = and $enabled .svc.autoscaling -}}
{{- end -}}
{{- if $enabled }}true{{ end -}}
{{- end }}
