/*
 * Contact form validation UX
 * - Shows red * for required fields
 * - Changes to green ✓ when filled correctly
 * - Shows error messages only after server validation
 * - Does NOT prevent form submission - server is authoritative
 */
(function() {
  "use strict";

  const form = document.getElementById('contact-form');
  if (!form) return;

  // Required field configurations
  const requiredFields = {
    'name': { minLength: 2 },
    'reach': { minLength: 5 },
    'need': { minLength: 10 },
    'captcha': { minLength: 1 }
  };

  // Get input name from field name
  const getInputName = (fieldName) => {
    return fieldName === 'captcha' ? 'captcha_answer' : fieldName.charAt(0).toUpperCase() + fieldName.slice(1);
  };

  // Update indicator for a field
  const updateIndicator = (fieldName, isValid) => {
    const indicator = form.querySelector(`[data-field="${fieldName}"]`);
    if (!indicator) return;
    
    if (isValid) {
      indicator.textContent = '✓';
      indicator.classList.remove('text-red-600');
      indicator.classList.add('text-green-600');
    } else {
      indicator.textContent = '*';
      indicator.classList.remove('text-green-600');
      indicator.classList.add('text-red-600');
    }
  };

  // Check if field is valid
  const isFieldValid = (fieldName) => {
    const inputName = getInputName(fieldName);
    const field = form.querySelector(`[name="${inputName}"]`);
    if (!field) return false;
    
    const value = field.value.trim();
    const config = requiredFields[fieldName];
    return value.length >= (config?.minLength || 1);
  };

  // Add input listeners to required fields
  Object.keys(requiredFields).forEach(fieldName => {
    const inputName = getInputName(fieldName);
    const field = form.querySelector(`[name="${inputName}"]`);
    if (!field) return;

    field.addEventListener('input', () => {
      const isValid = isFieldValid(fieldName);
      updateIndicator(fieldName, isValid);
    });

    // Set initial state based on existing value
    if (field.value.trim().length > 0) {
      const isValid = isFieldValid(fieldName);
      updateIndicator(fieldName, isValid);
    }
  });

  // Do NOT prevent form submission - let server validate
  // The form will POST to the server, which will validate all fields
  // Server will redirect back with errors if validation fails
  // This allows proper browser history and refresh behavior
})();
