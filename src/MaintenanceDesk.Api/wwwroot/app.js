const $ = id => document.getElementById(id);
const labels = {Open:'Open', InProgress:'In progress', Resolved:'Resolved'};
const state = {status:'', priority:'', page:1, size:6, items:[], selected:null, loading:false, saving:false, stale:false, request:0};
let toastTimer;
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const date = value => new Date(value).toLocaleDateString('en-IE', {day:'2-digit',month:'short',year:'numeric'});
function badge(ticket) { return `<span class="badge status-${ticket.status}"><i class="dot ${ticket.status==='Open'?'orange':ticket.status==='InProgress'?'blue':'green'}"></i>${labels[ticket.status]}</span>`; }
async function api(path, options={}) {
  const response = await fetch(path, {...options, headers:{'Content-Type':'application/json',...options.headers}});
  if (!response.ok) {
    const body = await response.json().catch(()=>({}));
    const error = new Error(response.status===409 ? 'This work order has changed. Close this window and reopen the order to use its latest details.' : body.title || `Request failed (${response.status}). Please try again.`);
    error.status = response.status; throw error;
  }
  return response.json();
}
function notify(message) { clearTimeout(toastTimer); $('toast').textContent=message; $('toast').hidden=false; toastTimer=setTimeout(()=>$('toast').hidden=true,4500); }
async function load() {
  const request = ++state.request; state.loading=true;
  $('previous').disabled=true; $('next').disabled=true; $('refresh').disabled=true; $('page-error').hidden=true;
  const query = new URLSearchParams({page:state.page,pageSize:state.size});
  if(state.status) query.set('status',state.status);
  if(state.priority) query.set('priority',state.priority);
  try {
    const [list,summary] = await Promise.all([api('/tickets?'+query),api('/tickets/summary')]);
    if(request!==state.request) return;
    if(!list.items.length && list.total>0 && state.page>1) {state.page=Math.ceil(list.total/state.size);return load();}
    state.items=list.items;
    for (const key of ['total','open','inProgress','resolved']) $(key).textContent=summary[key];
    $('connection').textContent='Connected locally'; $('connection').classList.add('live');
    $('rows').innerHTML=list.items.map(t=>`<tr><td><strong>${escapeHtml(t.title)}</strong><small><span class="ticket-code">#${t.id.slice(0,6).toUpperCase()}</span> ${escapeHtml(t.equipment)}</small></td><td><span class="priority-badge priority-${t.priority}">${t.priority}</span></td><td>${badge(t)}</td><td>${date(t.createdAtUtc)}</td><td><button class="view" data-id="${t.id}" aria-label="View ${escapeHtml(t.title)}">View ↗</button></td></tr>`).join('');
    $('empty').hidden=list.items.length>0;
    $('pagination').textContent=list.total ? `Showing ${(state.page-1)*state.size+1}–${Math.min(state.page*state.size,list.total)} of ${list.total} work orders` : '0 work orders';
    $('previous').disabled=state.page<=1; $('next').disabled=state.page*state.size>=list.total;
  } catch(error) {
    if(request!==state.request) return;
    $('page-error').textContent='Could not load work orders. Check that MaintenanceDesk is running, then select Refresh.';
    $('page-error').hidden=false; $('connection').textContent='Connection unavailable'; $('connection').classList.remove('live');
    $('pagination').textContent='Unable to refresh';
    state.items=[];
    $('rows').replaceChildren();
    $('empty').hidden=true;
    for (const key of ['total','open','inProgress','resolved']) $(key).textContent='—';
  } finally { if(request===state.request) {state.loading=false;$('refresh').disabled=false;} }
}
function showEditor(ticket=null) {
  state.selected=ticket; state.stale=false; $('ticket-form').reset(); $('form-error').hidden=true; $('save').disabled=false;
  $('create-fields').hidden=!!ticket; $('ticket-detail').hidden=!ticket;
  for(const input of $('create-fields').querySelectorAll('input,select')) input.disabled=!!ticket;
  const resolving=ticket?.status==='InProgress';
  $('resolution-label').hidden=!resolving;
  $('ticket-form').elements.resolution.required=resolving;
  $('modal-title').textContent=ticket?'Work order details':'New work order';
  $('save').textContent=ticket ? (resolving?'Resolve work order':ticket.status==='Resolved'?'Reopen work order':'Start work') : 'Create work order';
  if(ticket) $('ticket-detail').innerHTML=`<h3 class="detail-title">${escapeHtml(ticket.title)}</h3><p class="detail-equipment">${escapeHtml(ticket.equipment)}</p><div class="detail-meta">${badge(ticket)}<span class="priority-badge priority-${ticket.priority}">${ticket.priority} priority</span></div><p class="detail-date">Created ${date(ticket.createdAtUtc)} · Updated ${date(ticket.updatedAtUtc)}</p>${ticket.resolution?`<h4>Resolution</h4><div class="detail-notes">${escapeHtml(ticket.resolution)}</div>`:''}${ticket.status==='Resolved'?'<p class="detail-date">Reopening starts a new repair and clears the current resolution notes.</p>':''}`;
  $('editor').showModal();
}
function closeEditor(){if(!state.saving)$('editor').close();}
$('new-ticket').addEventListener('click',()=>showEditor());
$('close-modal').addEventListener('click',closeEditor); $('cancel').addEventListener('click',closeEditor);
$('editor').addEventListener('cancel',event=>{if(state.saving)event.preventDefault();});
$('rows').addEventListener('click',event=>{const button=event.target.closest('[data-id]'); if(button)showEditor(state.items.find(t=>t.id===button.dataset.id));});
$('ticket-form').addEventListener('submit',async event=>{
  event.preventDefault(); if(state.saving || state.stale)return;
  const fields=event.target.elements;
  if(!state.selected && (!fields.title.value.trim() || !fields.equipment.value.trim())) { $('form-error').textContent='Enter an issue title and equipment name.'; $('form-error').hidden=false; return; }
  if(state.selected?.status==='InProgress' && !fields.resolution.value.trim()) {$('form-error').textContent='Describe how the issue was resolved.';$('form-error').hidden=false;return;}
  state.saving=true;$('save').disabled=true;$('form-error').hidden=true;
  try {
    if(state.selected) {
      const ticket=state.selected;
      await api(`/tickets/${ticket.id}/status`,{method:'PATCH',body:JSON.stringify({status:ticket.status==='InProgress'?'Resolved':'InProgress',version:ticket.version,resolution:fields.resolution.value.trim()})});
      notify(ticket.status==='InProgress'?'Work order resolved.':ticket.status==='Resolved'?'Work order reopened.':'Work started.');
    } else {
      await api('/tickets',{method:'POST',body:JSON.stringify({title:fields.title.value.trim(),equipment:fields.equipment.value.trim(),priority:fields.priority.value})});
      state.page=1;state.status='';state.priority='';$('priority').value='';updateTabs();notify('Work order created.');
    }
    $('editor').close(); await load();
  } catch(error) {
    $('form-error').textContent=error.status ? error.message : 'Connection lost. Refresh the work orders to check whether your change was saved before trying again.';
    $('form-error').hidden=false;
    if(error.status===409) {
      state.stale=true;
      await load();
    }
  } finally {
    state.saving=false;
    $('save').disabled=state.stale;
  }
});
function updateTabs(){document.querySelectorAll('[data-status]').forEach(button=>{const active=button.dataset.status===state.status;button.classList.toggle('active',active);button.setAttribute('aria-pressed',String(active));});}
document.querySelectorAll('[data-status]').forEach(button=>button.addEventListener('click',()=>{state.status=button.dataset.status;state.page=1;updateTabs();load();}));
$('priority').addEventListener('change',event=>{state.priority=event.target.value;state.page=1;load();});
$('previous').addEventListener('click',()=>{if(!state.loading&&state.page>1){state.page--;load();}});
$('next').addEventListener('click',()=>{if(!state.loading){state.page++;load();}});
$('refresh').addEventListener('click',load);
load();
