<script setup>
import { ref, watch, computed } from "vue";
import Dialog from "primevue/dialog";
import Button from "primevue/button";
import InputText from "primevue/inputtext";
import Textarea from "primevue/textarea";
import Select from "primevue/select";
import Checkbox from "primevue/checkbox";
import Divider from "primevue/divider";

const props = defineProps({
  visible: Boolean,
  doc: Object,
  isEditing: Boolean,
  userInfo: Object,
});

const emit = defineEmits(["update:visible", "save"]);

const gameOptions = [
  { label: "Genshin Impact", value: "Genshin" },
  { label: "Honkai: Star Rail", value: "HonkaiStarRail" },
  { label: "Zenless Zone Zero", value: "ZenlessZoneZero" },
  { label: "Honkai Impact 3rd", value: "HonkaiImpact3" },
  { label: "Miscellaneous", value: "Unsupported" },
];

const form = ref({
  name: "",
  description: "",
  game: "Genshin",
  parameters: [],
  examples: [],
});

const newParam = ref({
  name: "",
  type: "text",
  description: "",
  required: false,
});

const newExample = ref("");

const paramTypeOptions = [
  { label: "text", value: "text" },
  { label: "number", value: "number" },
  { label: "boolean", value: "boolean" },
  { label: "server", value: "server" },
];

const resetForm = () => {
  form.value = {
    name: "",
    description: "",
    game: "Genshin",
    parameters: [],
    examples: [],
  };
  newParam.value = { name: "", type: "text", description: "", required: false };
  newExample.value = "";
};

watch(
  () => props.visible,
  (visible) => {
    if (visible) {
      if (props.doc && props.isEditing) {
        form.value = {
          name: props.doc.name || "",
          description: props.doc.description || "",
          game: props.doc.game || "Genshin",
          parameters: props.doc.parameters ? [...props.doc.parameters] : [],
          examples: props.doc.examples ? [...props.doc.examples] : [],
        };
      } else {
        resetForm();
      }
    }
  },
);

const addParameter = () => {
  if (!newParam.value.name.trim()) return;
  form.value.parameters.push({
    name: newParam.value.name.trim(),
    type: newParam.value.type,
    description: newParam.value.description.trim(),
    required: newParam.value.required,
  });
  newParam.value = { name: "", type: "text", description: "", required: false };
};

const removeParameter = (index) => {
  form.value.parameters.splice(index, 1);
};

const addExample = () => {
  if (!newExample.value.trim()) return;
  form.value.examples.push(newExample.value.trim());
  newExample.value = "";
};

const removeExample = (index) => {
  form.value.examples.splice(index, 1);
};

const handleSubmit = () => {
  if (!form.value.name.trim() || !form.value.description.trim()) {
    return;
  }
  emit("save", { ...form.value });
};

const handleClose = () => {
  emit("update:visible", false);
};

const canEditGame = computed(() => {
  if (props.userInfo?.isSuperAdmin) return true;
  return false;
});
</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="emit('update:visible', $event)"
    :header="isEditing ? 'Edit Documentation' : 'Add Documentation'"
    modal
    :style="{ width: '90%', maxWidth: '700px' }"
  >
    <form @submit.prevent="handleSubmit" class="flex flex-col">
      <div class="flex flex-col gap-4">
        <div class="flex flex-col gap-2">
          <label class="font-semibold text-zinc-200">Command Name</label>
          <InputText v-model="form.name" placeholder="e.g., build" required />
        </div>

        <div class="flex flex-col gap-2">
          <label class="font-semibold text-zinc-200">Description</label>
          <Textarea
            v-model="form.description"
            placeholder="Brief description of what this command does"
            rows="3"
            required
          />
        </div>

        <div class="flex flex-col gap-2">
          <label class="font-semibold text-zinc-200">Game</label>
          <Select
            v-model="form.game"
            :options="gameOptions"
            optionLabel="label"
            optionValue="value"
            :disabled="isEditing && !canEditGame"
          />
        </div>

        <Divider />

        <div class="flex flex-col gap-2">
          <label class="font-semibold text-zinc-200">Parameters</label>
          <div class="flex gap-2 items-start flex-wrap p-3 bg-white/5 rounded-md border border-white/10">
            <div class="flex flex-col gap-2 flex-1 min-w-0">
              <div class="flex gap-2 items-center flex-wrap w-full">
                <InputText
                  v-model="newParam.name"
                  placeholder="Parameter name"
                  class="flex-1 min-w-0"
                />
                <Select
                  v-model="newParam.type"
                  :options="paramTypeOptions"
                  optionLabel="label"
                  optionValue="value"
                />
                <div class="flex items-center gap-2 px-2">
                  <Checkbox
                    v-model="newParam.required"
                    binary
                    inputId="param-required"
                  />
                  <label for="param-required" class="text-sm text-zinc-300">Required</label>
                </div>
              </div>
              <InputText
                v-model="newParam.description"
                placeholder="Description"
                class="w-full"
              />
            </div>
            <Button
              type="button"
              icon="pi pi-plus"
              size="small"
              @click="addParameter"
              :disabled="!newParam.name.trim()"
              class="shrink-0"
            />
          </div>
          <div v-if="form.parameters.length" class="flex flex-col gap-2 mt-2">
            <div
              v-for="(param, index) in form.parameters"
              :key="index"
              class="flex items-center gap-2 px-3 py-2 bg-white/5 rounded-md border border-white/10"
            >
              <span class="font-semibold font-mono text-white">{{ param.name }}</span>
              <span class="text-xs px-1.5 py-0.5 bg-emerald-500/20 text-emerald-200 rounded font-mono">{{ param.type }}</span>
              <span v-if="param.required" class="text-[0.65rem] px-1.5 py-0.5 bg-orange-500/20 text-orange-400 rounded uppercase font-semibold"
                >Required</span
              >
              <span v-if="param.description" class="flex-1 text-zinc-400 text-sm">{{ param.description }}</span>
              <Button
                type="button"
                icon="pi pi-times"
                severity="danger"
                text
                size="small"
                @click="removeParameter(index)"
              />
            </div>
          </div>
        </div>

        <Divider />

        <div class="flex flex-col gap-2">
          <label class="font-semibold text-zinc-200">Examples</label>
          <div class="flex gap-2 items-center">
            <InputText
              v-model="newExample"
              placeholder="Example command usage"
              class="flex-1"
              @keyup.enter="addExample"
            />
            <Button
              type="button"
              icon="pi pi-plus"
              size="small"
              @click="addExample"
              :disabled="!newExample.trim()"
            />
          </div>
          <div v-if="form.examples.length" class="flex flex-col gap-2 mt-2">
            <div
              v-for="(example, index) in form.examples"
              :key="index"
              class="flex items-center gap-2 px-3 py-2 bg-white/5 rounded-md border border-white/10"
            >
              <code class="flex-1 font-mono text-sm text-emerald-300/80">{{ example }}</code>
              <Button
                type="button"
                icon="pi pi-times"
                severity="danger"
                text
                size="small"
                @click="removeExample(index)"
              />
            </div>
          </div>
        </div>
      </div>

      <div class="flex justify-end gap-2 mt-6">
        <Button
          type="button"
          label="Cancel"
          severity="secondary"
          @click="handleClose"
        />
        <Button
          type="submit"
          :label="isEditing ? 'Update' : 'Create'"
          :disabled="!form.name.trim() || !form.description.trim()"
        />
      </div>
    </form>
  </Dialog>
</template>


