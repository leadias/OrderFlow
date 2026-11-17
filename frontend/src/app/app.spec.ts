import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { OrdersApi } from './orders-api';

describe('App', () => {
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpTesting.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should show login when no session exists', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpTesting.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Iniciar sesión');
    expect(compiled.querySelector('nav')).toBeNull();
  });

  it('should show inventory and load an existing product for adjustment', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpTesting.expectOne('/api/auth/me').flush({ email: 'admin@orderflow.local' });
    fixture.detectChanges();
    httpTesting.expectOne('/api/orders').flush([]);
    fixture.detectChanges();

    const inventoryTab = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('nav button'),
    ).find((button) => button.textContent?.trim() === 'Inventario');
    inventoryTab?.dispatchEvent(new MouseEvent('click'));
    fixture.detectChanges();
    httpTesting.expectOne('/api/inventory').flush([
      { productId: 'SKU-001', quantityAvailable: 8 },
    ]);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent).toContain('Inventario');
    expect(element.textContent).toContain('SKU-001');
  });

  it('should send inventory updates to the API', () => {
    const api = TestBed.inject(OrdersApi);
    api.setInventory('SKU-001', 8).subscribe();

    const request = httpTesting.expectOne('/api/inventory/SKU-001');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ quantityAvailable: 8 });
    request.flush({ productId: 'SKU-001', quantityAvailable: 8 });
  });

  it('should send login credentials to the API', () => {
    const api = TestBed.inject(OrdersApi);
    api.login('admin@orderflow.local', 'local-password').subscribe();

    const request = httpTesting.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'admin@orderflow.local', password: 'local-password' });
    request.flush(null);
  });
});
