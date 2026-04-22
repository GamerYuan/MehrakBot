<script setup>
import Dialog from "primevue/dialog";
import ProgressSpinner from "primevue/progressspinner";
import GameTag from "./GameTag.vue";

const props = defineProps({
  visible: Boolean,
  doc: Object,
  loading: Boolean,
});

const emit = defineEmits(["update:visible"]);

const handleClose = () => {
  emit("update:visible", false);
};
</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="emit('update:visible', $event)"
    :header="doc?.name || 'Documentation'"
    modal
    :style="{ width: '90%', maxWidth: '600px' }"
    class="doc-detail-modal"
  >
    <template v-if="loading">
      <div
        class="flex flex-col items-center justify-center gap-4 py-12 text-zinc-400"
      >
        <ProgressSpinner style="width: 50px; height: 50px" strokeWidth="4" />
        <span>Loading details...</span>
      </div>
    </template>
    <template v-else-if="doc">
      <div class="flex flex-col gap-6">
        <div class="flex items-center">
          <GameTag :game="doc.game" />
        </div>

        <div class="flex flex-col gap-2">
          <h4
            class="m-0 text-sm font-semibold text-emerald-400/80 uppercase tracking-widest"
          >
            Description
          </h4>
          <p class="m-0 text-zinc-300 leading-relaxed">{{ doc.description }}</p>
        </div>

        <div v-if="doc.name" class="flex flex-col gap-2">
          <h4
            class="m-0 text-sm font-semibold text-emerald-400/80 uppercase tracking-widest"
          >
            Usage
          </h4>
          <code
            class="block p-4 bg-black/40 rounded-lg font-mono text-sm text-emerald-300/80 break-all leading-relaxed"
            >/{{ doc.name
            }}<template v-if="doc.parameters?.length"
              ><template v-for="param in doc.parameters" :key="param.name"
                ><template v-if="param.required">
                  &lt;{{ param.name }}&gt;</template
                ><template v-else> [{{ param.name }}]</template></template
              ></template
            ></code
          >
        </div>

        <div v-if="doc.parameters?.length" class="flex flex-col gap-2">
          <h4
            class="m-0 text-sm font-semibold text-emerald-400/80 uppercase tracking-widest"
          >
            Parameters
          </h4>
          <div class="flex flex-col gap-3">
            <div
              v-for="param in doc.parameters"
              :key="param.name"
              class="p-4 bg-white/5 rounded-lg border border-white/10"
            >
              <div class="flex items-center gap-2 flex-wrap mb-2">
                <span class="font-semibold text-white font-mono">{{
                  param.name
                }}</span>
                <span
                  class="text-xs px-2 py-0.5 bg-emerald-500/20 text-emerald-200 rounded font-mono"
                  >{{ param.type }}</span
                >
                <span
                  v-if="param.required"
                  class="text-[0.65rem] px-1.5 py-0.5 bg-orange-500/20 text-orange-400 rounded uppercase font-semibold"
                  >Required</span
                >
              </div>
              <p v-if="param.description" class="m-0 text-sm text-zinc-400">
                {{ param.description }}
              </p>
            </div>
          </div>
        </div>

        <div v-if="doc.examples?.length" class="flex flex-col gap-2">
          <h4
            class="m-0 text-sm font-semibold text-emerald-400/80 uppercase tracking-widest"
          >
            Examples
          </h4>
          <code
            class="block p-4 bg-black/40 rounded-lg font-mono text-sm text-emerald-300/80 break-all leading-[1.8]"
            ><template v-for="(example, index) in doc.examples" :key="index"
              >{{ example
              }}<br v-if="index < doc.examples.length - 1" /></template
          ></code>
        </div>
      </div>
    </template>
  </Dialog>
</template>

<style scoped>
/* Scoped styles removed in favor of Tailwind classes */
</style>
