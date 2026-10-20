import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './register.component.html',
  styleUrl: './register.component.css'
})
export class RegisterComponent {
  submitted = false;
  errorMessage = '';
  successMessage = '';
  isLoading = false;

  registerForm;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService
  ) {
    this.registerForm = this.fb.nonNullable.group({
      fullName: ['', [
        Validators.required,
        Validators.minLength(2),
        Validators.maxLength(150)
      ]],
      email: ['', [
        Validators.required,
        Validators.email
      ]],
      phoneNumber: [''],
      password: ['', [
        Validators.required,
        Validators.minLength(8)
      ]],
      role: ['Student', Validators.required]
    });
  }

  onSubmit(): void {
    this.submitted = true;
    this.errorMessage = '';
    this.successMessage = '';

    if (this.registerForm.invalid) {
      this.registerForm.markAllAsTouched();
      return;
    }

    this.isLoading = true;

    const formValue = this.registerForm.getRawValue();

    this.authService.register({
      fullName: formValue.fullName.trim(),
      email: formValue.email.trim(),
      password: formValue.password,
      role: formValue.role as 'Student' | 'Mentor'
    }).subscribe({
      next: () => {
        this.successMessage =
          'Your account has been created successfully. You can now log in.';
        this.registerForm.reset({
          fullName: '',
          email: '',
          phoneNumber: '',
          password: '',
          role: 'Student'
        });
        this.submitted = false;
        this.isLoading = false;
      },
      error: (error: HttpErrorResponse) => {
        this.isLoading = false;

        if (error.status === 0) {
          this.errorMessage =
            'Cannot connect to the server. Make sure the DoubtConnect API is running.';
        } else if (error.status === 400) {
          this.errorMessage =
            error.error?.message ||
            'Registration failed. Check your details or whether this email is already registered.';
        } else {
          this.errorMessage =
            error.error?.message ||
            'Something went wrong. Please try again.';
        }
      }
    });
  }
}