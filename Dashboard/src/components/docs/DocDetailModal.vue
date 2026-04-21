<script setup>
import Dialog from "primevue/dialog";
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
    <template v-if="doc">
      <div v-if="loading" class="loading-container">
        <i class="pi pi-spinner pi-spin"></i>
        <span>Loading details...</span>
      </div>
      <div v-else class="doc-detail-content">
        <div class="detail-header">
          <GameTag :game="doc.game" />
        </div>

        <div class="detail-section">
          <h4>Description</h4>
          <p>{{ doc.description }}</p>
        </div>

        <div v-if="doc.name" class="detail-section">
          <h4>Usage</h4>
          <code class="usage-block"
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

        <div v-if="doc.parameters?.length" class="detail-section">
          <h4>Parameters</h4>
          <div class="parameters-list">
            <div
              v-for="param in doc.parameters"
              :key="param.name"
              class="parameter-item"
            >
              <div class="param-header">
                <span class="param-name">{{ param.name }}</span>
                <span class="param-type">{{ param.type }}</span>
                <span v-if="param.required" class="param-required"
                  >Required</span
                >
              </div>
              <p v-if="param.description" class="param-description">
                {{ param.description }}
              </p>
            </div>
          </div>
        </div>

        <div v-if="doc.examples?.length" class="detail-section">
          <h4>Examples</h4>
          <code class="examples-block"
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
.doc-detail-content {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.detail-header {
  display: flex;
  align-items: center;
}

.detail-section {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.detail-section h4 {
  margin: 0;
  font-size: 0.875rem;
  font-weight: 600;
  color: #6ee7b7;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.detail-section p {
  margin: 0;
  color: #ccc;
  line-height: 1.6;
}

.parameters-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.parameter-item {
  padding: 0.75rem;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.06);
}

.param-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-bottom: 0.25rem;
}

.param-name {
  font-weight: 600;
  color: #fff;
  font-family: monospace;
}

.param-type {
  font-size: 0.75rem;
  padding: 0.15rem 0.4rem;
  background: rgba(88, 101, 242, 0.2);
  color: #a0a0ff;
  border-radius: 4px;
  font-family: monospace;
}

.param-required {
  font-size: 0.65rem;
  padding: 0.1rem 0.35rem;
  background: rgba(255, 107, 0, 0.2);
  color: #ff9500;
  border-radius: 4px;
  text-transform: uppercase;
  font-weight: 600;
}

.param-description {
  margin: 0;
  font-size: 0.85rem;
  color: #888;
}

.usage-block {
  display: block;
  padding: 0.75rem 1rem;
  background: rgba(0, 0, 0, 0.3);
  border-radius: 8px;
  font-family: monospace;
  font-size: 0.9rem;
  color: #5dc39b;
  word-break: break-all;
  line-height: 1.6;
}

.examples-block {
  display: block;
  padding: 0.75rem 1rem;
  background: rgba(0, 0, 0, 0.3);
  border-radius: 8px;
  font-family: monospace;
  font-size: 0.9rem;
  color: #5dc39b;
  word-break: break-all;
  line-height: 1.8;
}

.loading-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  padding: 2rem;
  color: #888;
}

.loading-container i {
  font-size: 1.5rem;
  color: #5dc39b;
}
</style>
